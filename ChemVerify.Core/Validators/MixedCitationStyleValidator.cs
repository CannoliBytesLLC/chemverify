using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MixedCitationStyleValidator : IValidator
{
    // DOI citation pattern
    private static readonly Regex DoiCitationRegex = new(
        @"10\.\d{4,9}/[^\s]+",
        RegexOptions.Compiled);

    // Author-year citation pattern, e.g. "(Smith, 2020)", "(Smith et al., 2020)"
    private static readonly Regex AuthorYearRegex = new(
        @"\(\s*[A-Z][a-z]+(?:\s+et\s+al\.?)?\s*[,;]\s*\d{4}\s*\)",
        RegexOptions.Compiled);

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();

        if (string.IsNullOrEmpty(text))
        {
            return findings;
        }

        bool hasDoi = DoiCitationRegex.IsMatch(text);
        bool hasAuthorYear = AuthorYearRegex.IsMatch(text);

        if (hasDoi && hasAuthorYear)
        {
            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = nameof(MixedCitationStyleValidator),
                Status = ValidationStatus.Unverified,
                Message = "[TEXT.CITATION_TRACEABILITY_WEAK] Document mixes DOI citations and author-year citations, reducing traceability.",
                Confidence = 0.85,
                Kind = FindingKind.CitationTraceabilityWeak
            });
        }

        return findings;
    }
}
