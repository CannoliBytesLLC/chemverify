using System.Text.Json;
using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MalformedChemicalTokenValidator : IValidator
{
    // Chemical name followed by empty parentheses, e.g. "benzene ()" or "NaOH ()"
    private static readonly Regex EmptyParensRegex = new(
        @"\b[A-Z][a-zA-Z0-9]{1,}\s*\(\s*\)",
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
