using Aegis.Core.Enums;
using Aegis.Core.Interfaces;
using Aegis.Core.Models;
using Aegis.Infrastructure.Extractors;
using Aegis.Infrastructure.Persistence;

namespace Aegis.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly IModelConnector _modelConnector;
    private readonly IClaimExtractor _claimExtractor;
    private readonly IEnumerable<IValidator> _validators;
    private readonly IHashService _hashService;
    private readonly ICanonicalizer _canonicalizer;
    private readonly IRiskScorer _riskScorer;
    private readonly AegisRunRepository _repository;

    public AuditService(
        IModelConnector modelConnector,
        IClaimExtractor claimExtractor,
        IEnumerable<IValidator> validators,
        IHashService hashService,
        ICanonicalizer canonicalizer,
        IRiskScorer riskScorer,
        AegisRunRepository repository)
    {
        _modelConnector = modelConnector;
        _claimExtractor = claimExtractor;
        _validators = validators;
        _hashService = hashService;
        _canonicalizer = canonicalizer;
        _riskScorer = riskScorer;
        _repository = repository;
    }

    public async Task<AuditArtifact> CreateRunAndAuditAsync(RunCommand command, CancellationToken ct)
    {
        // 1. Create the run shell
        AiRun run = new()
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTimeOffset.UtcNow,
            Status = RunStatus.Created,
            UserId = command.UserId,
            ModelName = command.ModelName,
            PolicyProfile = command.PolicyProfile,
            ConnectorName = command.ConnectorName ?? _modelConnector.GetType().Name,
            ModelVersion = command.ModelVersion,
            ParametersJson = command.ParametersJson,
            Prompt = command.Prompt
        };

        // Resolve policy profile into concrete pipeline settings
        PolicySettings policySettings = PolicyProfileResolver.Resolve(command.PolicyProfile);

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

            // 4. Compute tamper-evidence hash (canonicalize inputs first)
            string canonicalPrompt = _canonicalizer.Canonicalize(run.Prompt);
            string canonicalOutput = _canonicalizer.Canonicalize(run.Output);
            string hashInput = string.Concat(
                run.PreviousHash ?? string.Empty,
                canonicalPrompt,
                canonicalOutput,
                run.CreatedUtc.ToString("O"),
                run.ModelName);
            run.CurrentHash = _hashService.ComputeHash(hashInput);

            // 5. Extract claims (fault-tolerant via CompositeClaimExtractor)
            IReadOnlyList<ExtractedClaim> extractedClaims = _claimExtractor.Extract(run.Id, run.Output);
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
                && !string.IsNullOrEmpty(run.Output))
            {
                string reformatPrompt =
                    "Reformat ONLY the following text into a JSON array of claims. "
                    + "Each claim has: {\"type\",\"rawText\",\"value\",\"unit\"}. No prose.\n\n"
                    + run.Output;

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

            // 6. Run validators (each validator is individually guarded)
            foreach (IValidator validator in _validators)
            {
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

            // 7. Compute risk score via centralised scorer
            run.RiskScore = _riskScorer.ComputeScore(allFindings);

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
            GeneratedUtc = DateTimeOffset.UtcNow
        };

        // Compute ArtifactHash over canonical JSON of the artifact
        string canonicalArtifactJson = _canonicalizer.CanonicalizeJson(new
        {
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
}
