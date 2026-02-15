using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class IncompleteScientificClaimValidator : IValidator
{
    // "e.g." followed by a unit abbreviation but no number, e.g. "e.g. mL" or "e.g., °C"
    private static readonly Regex EgWithoutNumberRegex = new(
        @"e\.g\.[\s,]*(?:°C|mL|mg|g|mol|mmol|%|µL|µg|kPa|atm)\b(?!\s*\d)",
        RegexOptions.Compiled);

    // Comparative chain using ">" without a nearby citation marker in the same sentence
    // Captures sentences containing ">" used as a comparison operator
    private static readonly Regex ComparativeChainRegex = new(
        @"[^.!?]*\b\w+\s*>\s*\w+[^.!?]*[.!?]",
        RegexOptions.Compiled);

    private static readonly Regex CitationMarkerRegex = new(
        @"10\.\d{4,9}/|\(\s*[A-Z][a-z]+(?:\s+(?:&|and)\s+[A-Z][a-z]+)*(?:\s+et\s+al\.?)?\s*[,;]\s*\d{4}\s*\)|\[\d+\]",
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

        foreach (Match m in EgWithoutNumberRegex.Matches(text))
        {
            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = nameof(IncompleteScientificClaimValidator),
                Status = ValidationStatus.Fail,
                Message = $"[TEXT.UNSUPPORTED_OR_INCOMPLETE_CLAIM] \"e.g.\" followed by unit without numeric value: \"{m.Value.Trim()}\" at position {m.Index}.",
                Confidence = 0.75,
                Kind = FindingKind.UnsupportedOrIncompleteClaim,
                EvidenceRef = $"AnalyzedText:{m.Index}-{m.Index + m.Length}"
            });
        }

        foreach (Match m in ComparativeChainRegex.Matches(text))
        {
            string sentence = m.Value;
            if (!CitationMarkerRegex.IsMatch(sentence))
            {
                findings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ValidatorName = nameof(IncompleteScientificClaimValidator),
                    Status = ValidationStatus.Fail,
                    Message = $"[TEXT.UNSUPPORTED_OR_INCOMPLETE_CLAIM] Comparative chain (\">\") without nearby citation in sentence at position {m.Index}.",
                    Confidence = 0.7,
                    Kind = FindingKind.UnsupportedOrIncompleteClaim,
                    EvidenceRef = $"AnalyzedText:{m.Index}-{m.Index + m.Length}"
                });
            }
        }

        return findings;
    }
}
