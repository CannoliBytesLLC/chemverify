using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Extractors;

public class CompositeClaimExtractor : IClaimExtractor
{
    private readonly IReadOnlyList<IClaimExtractor> _extractors;
    private readonly List<ValidationFinding> _diagnosticFindings = new();

    public CompositeClaimExtractor(IEnumerable<IClaimExtractor> extractors)
    {
        // Filter out self to prevent recursion when resolved from DI as IEnumerable<IClaimExtractor>
        _extractors = extractors
            .Where(e => e is not CompositeClaimExtractor)
            .ToList();
    }

    /// <summary>
    /// Returns any diagnostic findings generated during the last Extract call
    /// (e.g. when an individual extractor threw an exception).
    /// </summary>
    public IReadOnlyList<ValidationFinding> DiagnosticFindings => _diagnosticFindings;

    public IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text)
    {
        List<ExtractedClaim> allClaims = new();
        _diagnosticFindings.Clear();

        foreach (IClaimExtractor extractor in _extractors)
        {
            try
            {
                IReadOnlyList<ExtractedClaim> claims = extractor.Extract(runId, text);
                allClaims.AddRange(claims);
            }
            catch (Exception ex)
            {
                _diagnosticFindings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ValidatorName = $"Extractor:{extractor.GetType().Name}",
                    Status = ValidationStatus.Unverified,
                    Message = $"Extractor failed: {ex.Message}",
                    Confidence = 0.0
                });
            }
        }

        return allClaims;
    }
}

