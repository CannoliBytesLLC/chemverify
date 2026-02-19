using System.Text.Json;
using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MalformedChemicalTokenValidator : IValidator
{
    // Any word followed by empty parentheses, e.g. "benzene ()" or "borohydride ()"
    private static readonly Regex EmptyParensRegex = new(
        @"\b[a-zA-Z][a-zA-Z0-9]{1,}\s*\(\s*\)",
        RegexOptions.Compiled);

    // Standalone °C without a preceding numeric value
    private static readonly Regex StandaloneDegreeCRegex = new(
        @"(?<!\d\s?)°C",
        RegexOptions.Compiled);

    // Dangling markdown/latex fragments: lone underscore not part of a word,
    // unmatched single backtick, or lone backslash followed by nothing useful
    private static readonly Regex DanglingFormattingRegex = new(
        @"(?<!\w)_(?!\w)|(?<![`])`(?![`])|\\(?=[,.\s]|$)",
        RegexOptions.Compiled);

    // Two or more consecutive horizontal spaces between word characters — suggests a dropped token.
    // Uses [ \t] instead of \s to avoid matching paragraph breaks (\n\n).
    private static readonly Regex DroppedTokenRegex = new(
        @"(?<=\w)[ \t]{2,}(?=\w)",
        RegexOptions.Compiled);

    // Empty bold markers: ** immediately followed by ** with at most whitespace between
    private static readonly Regex EmptyBoldRegex = new(
        @"\*\*\s*\*\*",
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

        foreach (Match m in EmptyParensRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m, "Chemical name followed by empty parentheses"));
        }

        foreach (Match m in StandaloneDegreeCRegex.Matches(text))
        {
            string payload = JsonSerializer.Serialize(new
            {
                expected = "temperature numeric value",
                examples = new[] { "0 °C", "25 °C", "-78 °C" },
                token = "°C"
            });
            findings.Add(BuildFinding(runId, m, "Standalone °C without numeric value", payload));
        }

        foreach (Match m in DanglingFormattingRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m, "Dangling markdown/LaTeX formatting fragment"));
        }

        foreach (Match m in DroppedTokenRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                "Consecutive spaces suggest a dropped chemical formula or token"));
        }

        foreach (Match m in EmptyBoldRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                "Empty bold marker — expected chemical name or formula between markers"));
        }

        return findings;
    }

    private static ValidationFinding BuildFinding(Guid runId, Match match, string detail, string? jsonPayload = null) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        ValidatorName = nameof(MalformedChemicalTokenValidator),
        Status = ValidationStatus.Fail,
        Message = $"[TEXT.MALFORMED_CHEMICAL_TOKEN] {detail}: \"{match.Value}\" at position {match.Index}.",
        Confidence = 0.8,
        Kind = FindingKind.MalformedChemicalToken,
        EvidenceRef = $"AnalyzedText:{match.Index}-{match.Index + match.Length}",
        JsonPayload = jsonPayload
    };
}
