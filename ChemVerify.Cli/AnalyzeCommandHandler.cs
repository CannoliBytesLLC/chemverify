using System.Text.Json;
using System.Text.Json.Serialization;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Reporting;
using ChemVerify.Core.Services;
using ChemVerify.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace ChemVerify.Cli;

/// <summary>
/// Handles the <c>analyze</c> command by running the core verification pipeline
/// without any database or infrastructure dependency.
/// </summary>
public static class AnalyzeCommandHandler
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Runs the full analyze pipeline. Returns a process exit code.
    /// </summary>
    public static async Task<int> ExecuteAsync(
        string path,
        string profile,
        string format,
        string? outPath,
        int maxInputChars,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
            {
                await stderr.WriteLineAsync($"Error: file not found \u2014 {path}");
                return ExitCodes.EngineError;
            }

            string text = await File.ReadAllTextAsync(path, ct);

            if (text.Length > maxInputChars)
            {
                await stderr.WriteLineAsync(
                    $"Error: input exceeds {maxInputChars} characters ({text.Length} found).");
                return ExitCodes.EngineError;
            }

            // Build lightweight DI container with Core services only (no Infrastructure)
            var services = new ServiceCollection();
            services.AddChemVerifyCore();
            using ServiceProvider sp = services.BuildServiceProvider();

            var (report, findings, validators) = RunPipeline(sp, text, profile);

            string output = format.Equals("sarif", StringComparison.OrdinalIgnoreCase)
                ? SarifExporter.Build(report, findings, path, text, validators)
                : JsonSerializer.Serialize(report, JsonOptions);

            if (outPath is not null)
            {
                string? dir = Path.GetDirectoryName(outPath);
                if (dir is not null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(outPath, output, ct);
            }
            else
            {
                await stdout.WriteAsync(output);
            }

            return MapExitCode(report.Severity);
        }
        catch (OperationCanceledException)
        {
            return ExitCodes.EngineError;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Engine error: {ex.Message}");
            return ExitCodes.EngineError;
        }
    }

    // ------------------------------------------------------------------
    // Pipeline — mirrors AuditService.VerifyTextAsync without persistence
    // ------------------------------------------------------------------

    internal static (ReportDto Report, IReadOnlyList<ValidationFinding> Findings, IReadOnlyList<IValidator> Validators) RunPipeline(
        IServiceProvider sp,
        string text,
        string profile)
    {
        IClaimExtractor claimExtractor = sp.GetRequiredService<IClaimExtractor>();
        List<IValidator> validators = [.. sp.GetServices<IValidator>()];
        IRiskScorer riskScorer = sp.GetRequiredService<IRiskScorer>();
        PolicyProfileResolver profileResolver = sp.GetRequiredService<PolicyProfileResolver>();

        Guid runId = Guid.NewGuid();
        AiRun run = new()
        {
            Id = runId,
            CreatedUtc = DateTimeOffset.UtcNow,
            Status = RunStatus.Completed,
            Mode = RunMode.VerifyOnly,
            InputText = text,
            Prompt = string.Empty,
            ModelName = "cli-verify",
            PolicyProfile = profile == "Default" ? null : profile
        };

        string analyzedText = run.GetAnalyzedText();

        // Extract claims
        List<ExtractedClaim> claims = [.. claimExtractor.Extract(runId, analyzedText)];
        List<ValidationFinding> allFindings = [];

        if (claimExtractor is CompositeClaimExtractor composite)
            allFindings.AddRange(composite.DiagnosticFindings);

        // Resolve policy profile
        PolicySettings policySettings = profileResolver.Resolve(run.PolicyProfile);

        // Run validators (policy-filtered)
        foreach (IValidator validator in validators)
        {
            string validatorName = validator.GetType().Name;

            if (policySettings.IncludedValidators.Count > 0 &&
                !policySettings.IncludedValidators.Contains(validatorName))
                continue;
            if (policySettings.ExcludedValidators.Contains(validatorName))
                continue;

            try
            {
                allFindings.AddRange(validator.Validate(runId, claims, run));
            }
            catch (Exception ex)
            {
                allFindings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ValidatorName = validatorName,
                    Status = ValidationStatus.Unverified,
                    Message = $"Validator failed: {ex.Message}",
                    Confidence = 0.0
                });
            }
        }

        // Enrich findings with evidence spans
        EnrichEvidenceSpans(allFindings, claims, run);

        // Compute risk score
        double riskScore = riskScorer.ComputeScore(allFindings, policySettings);

        // Build report
        ReportDto report = ReportBuilder.Build(
            riskScore, claims, allFindings,
            policyProfileName: run.PolicyProfile,
            policyProfileVersion: null);

        return (report, allFindings, validators);
    }

    private static void EnrichEvidenceSpans(
        List<ValidationFinding> findings,
        List<ExtractedClaim> claims,
        AiRun run)
    {
        string text = run.GetAnalyzedText();

        foreach (ValidationFinding f in findings)
        {
            if (f.EvidenceStartOffset is not null)
                continue;

            int start = -1, end = -1;
            int? stepIndex = null;
            string? entityKey = null;

            if (f.ClaimId is not null)
            {
                ExtractedClaim? claim = claims.FirstOrDefault(c => c.Id == f.ClaimId);
                if (claim is not null)
                {
                    if (EvidenceLocator.TryParse(claim.SourceLocator, out int cs, out int ce))
                    {
                        start = cs;
                        end = ce;
                    }
                    stepIndex = claim.StepIndex;
                    entityKey = claim.EntityKey;
                }
            }

            if (start < 0 && f.EvidenceRef is not null)
            {
                if (EvidenceLocator.TryParse(f.EvidenceRef, out int es, out int ee))
                {
                    start = es;
                    end = ee;
                }
            }

            if (start >= 0)
            {
                f.EvidenceStartOffset = start;
                f.EvidenceEndOffset = end;
                f.EvidenceSnippet ??= EvidenceLocator.ExtractSnippet(text, start, end);
            }
            if (stepIndex.HasValue)
                f.EvidenceStepIndex ??= stepIndex;
            if (entityKey is not null)
                f.EvidenceEntityKey ??= entityKey;
        }
    }

    internal static int MapExitCode(string severity) => severity switch
    {
        "Low" => ExitCodes.Ok,
        "Medium" => ExitCodes.Warning,
        "High" or "Critical" => ExitCodes.RiskHigh,
        _ => ExitCodes.Ok
    };
}