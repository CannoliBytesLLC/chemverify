using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Services;
using ChemVerify.Core.Validation;
using ChemVerify.Infrastructure.Persistence;

namespace ChemVerify.Infrastructure.Services;

public class AuditService : IAuditService
{

    private readonly IModelConnector _modelConnector;
    private readonly IClaimExtractor _claimExtractor;
    private readonly IEnumerable<IValidator> _validators;
    private readonly IHashService _hashService;
    private readonly ICanonicalizer _canonicalizer;
    private readonly IRiskScorer _riskScorer;
    private readonly ChemVerifyRunRepository _repository;
    private readonly PolicyProfileResolver _resolver;

    public AuditService(
        IModelConnector modelConnector,
        IClaimExtractor claimExtractor,
        IEnumerable<IValidator> validators,
        IHashService hashService,
        ICanonicalizer canonicalizer,
        IRiskScorer riskScorer,
        ChemVerifyRunRepository repository,
        PolicyProfileResolver resolver)
    {
        _modelConnector = modelConnector;
        _claimExtractor = claimExtractor;
        _validators = validators;
        _hashService = hashService;
        _canonicalizer = canonicalizer;
        _riskScorer = riskScorer;
        _repository = repository;
        _resolver = resolver;
    }

    public async Task<AuditArtifact> CreateRunAndAuditAsync(RunCommand command, CancellationToken ct)
    {
        // 1. Create the run shell
        AiRun run = new()
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTimeOffset.UtcNow,
            Status = RunStatus.Created,
            Mode = RunMode.GenerateAndVerify,
            UserId = command.UserId,
            ModelName = command.ModelName,
            PolicyProfile = command.PolicyProfile,
            ConnectorName = command.ConnectorName ?? _modelConnector.GetType().Name,
            ModelVersion = command.ModelVersion,
            ParametersJson = command.ParametersJson,
            Prompt = command.Prompt
        };

        // Resolve policy profile into concrete pipeline settings
        PolicySettings policySettings = _resolver.Resolve(command.PolicyProfile);

        // Policy may override the caller-supplied output contract
        OutputContract effectiveContract = policySettings.RequiredContract != OutputContract.FreeText
            ? policySettings.RequiredContract
            : command.OutputContract;

        List<ExtractedClaim> claims = new();
        List<ValidationFinding> allFindings = new();

        try
        {
            // 2. Call model connector
            string output = await _modelConnector.GenerateAsync(command.Prompt, ct);

            // 3. Set output
            run.Output = output;

            string analyzedText = run.GetAnalyzedText();

            // 4. Compute tamper-evidence hash (canonicalize inputs first)
            string canonicalPrompt = _canonicalizer.Canonicalize(run.Prompt);
            string canonicalOutput = _canonicalizer.Canonicalize(analyzedText);
            string hashInput = string.Concat(
                run.PreviousHash ?? string.Empty,
                EngineVersionProvider.Version,
                "GenerateAndVerify",
                run.PolicyProfile ?? string.Empty,
                canonicalPrompt,
                canonicalOutput,
                run.CreatedUtc.ToString("O"),
                run.ModelName);
            run.CurrentHash = _hashService.ComputeHash(hashInput);

            // 5. Extract claims (fault-tolerant via CompositeClaimExtractor)
            IReadOnlyList<ExtractedClaim> extractedClaims = _claimExtractor.Extract(run.Id, analyzedText);
            claims.AddRange(extractedClaims);

            // Collect any diagnostic findings from extraction failures
            if (_claimExtractor is CompositeClaimExtractor composite)
            {
                allFindings.AddRange(composite.DiagnosticFindings);
            }

            // 5b. Contract enforcement: if contract requires structured claims
            //     and extraction yielded nothing, retry with a reformatting prompt
            if (effectiveContract == OutputContract.JsonClaimsBlockV1
                && policySettings.AllowContractRetry
                && claims.Count == 0
                && !string.IsNullOrEmpty(analyzedText))
            {
                string reformatPrompt =
                    "Reformat ONLY the following text into a JSON array of claims. "
                    + "Each claim has: {\"type\",\"rawText\",\"value\",\"unit\"}. No prose.\n\n"
                    + analyzedText;

                string reformattedOutput = await _modelConnector.GenerateAsync(reformatPrompt, ct);
                run.Output = reformattedOutput;

                // Re-extract from the reformatted output
                IReadOnlyList<ExtractedClaim> retryExtracts = _claimExtractor.Extract(run.Id, reformattedOutput);
                claims.AddRange(retryExtracts);

                if (_claimExtractor is CompositeClaimExtractor retryComposite)
                {
                    allFindings.AddRange(retryComposite.DiagnosticFindings);
                }
            }

            // 6. Run validators via shared helper
            RunValidators(run, claims, allFindings, policySettings);

            // 6b. Enrich findings with evidence spans
            EnrichEvidenceSpans(allFindings, claims, run);

            // 7. Compute risk score via centralised scorer
            run.RiskScore = _riskScorer.ComputeScore(allFindings, policySettings);

            // Mark completed
            run.Status = RunStatus.Completed;
        }
        catch (Exception ex)
        {
            // Pipeline failure — persist what we have with Failed status
            run.Status = RunStatus.Failed;
            run.RiskScore = 1.0;

            if (string.IsNullOrEmpty(run.CurrentHash))
            {
                run.CurrentHash = _hashService.ComputeHash(
                    string.Concat(run.Prompt, run.CreatedUtc.ToString("O")));
            }

            allFindings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                ValidatorName = "Pipeline",
                Status = ValidationStatus.Fail,
                Message = $"Pipeline failed: {ex.Message}",
                Confidence = 1.0
            });
        }

        // 8. Persist run + claims + findings in one transaction
        await _repository.SaveRunAsync(run, claims, allFindings, ct);

        // 9. Build and return audit artifact
        AuditArtifact artifact = new()
        {
            RunId = run.Id,
            Run = run,
            Claims = claims,
            Findings = allFindings,
            EngineVersion = EngineVersionProvider.Version,
            GeneratedUtc = DateTimeOffset.UtcNow
        };

        // Compute ArtifactHash over canonical JSON of the artifact
        string canonicalArtifactJson = _canonicalizer.CanonicalizeJson(new
        {
            EngineVersion = EngineVersionProvider.Version,
            run.Mode,
            artifact.RunId,
            run.CurrentHash,
            run.CreatedUtc,
            run.ModelName,
            run.RiskScore,
            ClaimCount = claims.Count,
            FindingCount = allFindings.Count
        });
        artifact.ArtifactHash = _hashService.ComputeHash(canonicalArtifactJson);

        return artifact;
    }

    public async Task<AuditArtifact> VerifyTextAsync(string textToVerify, string? policyProfile, CancellationToken ct)
    {
        AiRun run = new()
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTimeOffset.UtcNow,
            Status = RunStatus.Completed,
            Mode = RunMode.VerifyOnly,
            InputText = textToVerify,
            Output = null,
            Prompt = string.Empty,
            ModelName = "verify-only",
            PolicyProfile = policyProfile
        };

        string analyzedText = run.GetAnalyzedText();

        // Compute tamper-evidence hash
        string canonicalText = _canonicalizer.Canonicalize(analyzedText);
        string hashInput = string.Concat(
            run.PreviousHash ?? string.Empty,
            EngineVersionProvider.Version,
            "VerifyOnly",
            policyProfile ?? string.Empty,
            canonicalText,
            run.CreatedUtc.ToString("O"),
            run.ModelName);
        run.CurrentHash = _hashService.ComputeHash(hashInput);

        // Extract claims
        List<ExtractedClaim> claims = new();
        List<ValidationFinding> allFindings = new();

        IReadOnlyList<ExtractedClaim> extractedClaims = _claimExtractor.Extract(run.Id, analyzedText);
        claims.AddRange(extractedClaims);

        if (_claimExtractor is CompositeClaimExtractor composite)
        {
            allFindings.AddRange(composite.DiagnosticFindings);
        }

        // Resolve policy profile
        PolicySettings policySettings = _resolver.Resolve(policyProfile);

        // Run validators
        RunValidators(run, claims, allFindings, policySettings);

        // Enrich findings with evidence spans
        EnrichEvidenceSpans(allFindings, claims, run);

        // Compute risk score
        run.RiskScore = _riskScorer.ComputeScore(allFindings, policySettings);

        // Persist
        await _repository.SaveRunAsync(run, claims, allFindings, ct);

        // Build artifact
        AuditArtifact artifact = new()
        {
            RunId = run.Id,
            Run = run,
            Claims = claims,
            Findings = allFindings,
            EngineVersion = EngineVersionProvider.Version,
            GeneratedUtc = DateTimeOffset.UtcNow
        };

        string canonicalArtifactJson = _canonicalizer.CanonicalizeJson(new
        {
            EngineVersion = EngineVersionProvider.Version,
            run.Mode,
            artifact.RunId,
            run.CurrentHash,
            run.CreatedUtc,
            run.ModelName,
            run.RiskScore,
            ClaimCount = claims.Count,
            FindingCount = allFindings.Count
        });
        artifact.ArtifactHash = _hashService.ComputeHash(canonicalArtifactJson);

        return artifact;
    }

    private void RunValidators(AiRun run, List<ExtractedClaim> claims, List<ValidationFinding> allFindings, PolicySettings? policy = null)
    {
        foreach (IValidator validator in _validators)
        {
            string validatorName = validator.GetType().Name;

            // Policy-based filtering
            if (policy is not null)
            {
                if (policy.IncludedValidators.Count > 0 && !policy.IncludedValidators.Contains(validatorName))
                    continue;
                if (policy.ExcludedValidators.Contains(validatorName))
                    continue;
            }

            try
            {
                IReadOnlyList<ValidationFinding> findings = validator.Validate(run.Id, claims, run);
                allFindings.AddRange(findings);
            }
            catch (Exception ex)
            {
                allFindings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = run.Id,
                    ValidatorName = validator.GetType().Name,
                    Status = ValidationStatus.Unverified,
                    Message = $"Validator failed: {ex.Message}",
                    Confidence = 0.0
                });
            }
        }
    }

    private static void EnrichEvidenceSpans(
        List<ValidationFinding> findings,
        List<ExtractedClaim> claims,
        AiRun run)
    {
        string text = run.GetAnalyzedText();

        foreach (ValidationFinding f in findings)
        {
            // Already enriched?
            if (f.EvidenceStartOffset is not null)
                continue;

            int start = -1, end = -1;
            int? stepIndex = null;
            string? entityKey = null;

            // Try claim-based evidence first
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

            // Fall back to EvidenceRef
            if (start < 0 && EvidenceLocator.TryParse(f.EvidenceRef, out int es, out int ee))
            {
                start = es;
                end = ee;
            }

            if (start >= 0 && end >= start)
            {
                f.EvidenceStartOffset = start;
                f.EvidenceEndOffset = end;
                f.EvidenceStepIndex = stepIndex;
                f.EvidenceEntityKey = entityKey;
                f.EvidenceSnippet = EvidenceLocator.ExtractSnippet(text, start, end);
            }
        }
    }
}

