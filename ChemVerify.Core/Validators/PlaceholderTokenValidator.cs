using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

/// <summary>
/// Detects placeholder or missing-value tokens that typically result from
/// copy-paste formatting loss — e.g. "under .", "ether ( mL)", "dried over ****",
/// or empty bold markers used as value slots. These are text-template artifacts,
/// not chemistry errors, so their risk contribution is capped via
/// <see cref="FindingKind.PlaceholderOrMissingToken"/>.
/// </summary>
public class PlaceholderTokenValidator : IValidator
{
    // "under ." / "under ," / "with ." — preposition followed immediately by punctuation
    private static readonly Regex PrepositionPunctuationRegex = new(
        @"\b(under|with|in|over|from|using|via|of)\s+([.,;:])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "( mL)" / "( mmol)" / "( g)" — parenthesized unit with no numeric value
    private static readonly Regex EmptyQuantityRegex = new(
        @"\(\s*(mL|mmol|mol|mg|g|kg|µ?L|°C|equiv|eq|h|min|atm|M)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "****" / "***" — three or more consecutive asterisks (placeholder masking)
    private static readonly Regex AsterisksPlaceholderRegex = new(
        @"\*{3,}",
        RegexOptions.Compiled);

    // "new  bond" / "form a new  bond" — common placeholder gap after "new" in chemistry text
    private static readonly Regex NewBlankBondRegex = new(
        @"\bnew\s{2,}bond\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "in % yield" / "of % ee" — standalone percent without a preceding number
    private static readonly Regex StandalonePercentRegex = new(
        @"\b(?:in|of)\s+%\s*(?:yield|conversion|ee)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "( % EtOAc/hexanes)" — percent inside parens with no preceding number
    private static readonly Regex ParenPercentRegex = new(
        @"\(\s*%\s+[A-Za-z]",
        RegexOptions.Compiled);

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();

        if (string.IsNullOrEmpty(text))
            return findings;

        foreach (Match m in PrepositionPunctuationRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                $"Preposition \"{m.Groups[1].Value}\" followed by punctuation — likely a missing value"));
        }

        foreach (Match m in EmptyQuantityRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                $"Parenthesized unit \"{m.Value}\" with no numeric value — likely a placeholder"));
        }

        foreach (Match m in AsterisksPlaceholderRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                "Consecutive asterisks suggest a masked or missing value"));
        }

        foreach (Match m in NewBlankBondRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                "\"new  bond\" with extra space — likely a dropped bond descriptor (e.g. C-C)"));
        }

        foreach (Match m in StandalonePercentRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                "Standalone \"%\" without a preceding number — likely a missing value"));
        }

        foreach (Match m in ParenPercentRegex.Matches(text))
        {
            findings.Add(BuildFinding(runId, m,
                "Percent inside parentheses without a preceding number — likely a missing composition value"));
        }

        return findings;
    }

    private static ValidationFinding BuildFinding(Guid runId, Match match, string detail) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        ValidatorName = nameof(PlaceholderTokenValidator),
        Status = ValidationStatus.Fail,
        Message = $"[TEXT.PLACEHOLDER_OR_MISSING_TOKEN] {detail}: \"{match.Value}\" at position {match.Index}.",
        Confidence = 0.7,
        Kind = FindingKind.PlaceholderOrMissingToken,
        EvidenceRef = $"AnalyzedText:{match.Index}-{match.Index + match.Length}"
    };
}
