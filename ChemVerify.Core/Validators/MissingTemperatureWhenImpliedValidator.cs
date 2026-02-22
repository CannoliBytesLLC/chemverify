using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MissingTemperatureWhenImpliedValidator : IValidator
{
    // Thermal-control verbs and phrases that imply a specific temperature is expected.
    // "ice bath" and "reflux" are NOT here — they are now extracted as SymbolicTemperature
    // claims by ReagentRoleExtractor and satisfy the temperature requirement.
    // "maintained at" is excluded because it can appear in non-thermal contexts
    // ("maintained under nitrogen").
    private static readonly Regex ImpliedTempRegex = new(
        @"\b(dropwise|exotherm(ic)?|cooling\s+bath|cryogenic|"
        + @"heated\s+to|cooled\s+to|warmed\s+to|kept\s+at\s+(?!rt\b|room|ambient)|"
        + @"stirred\s+at\s+(?!rt\b|room|ambient))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Explicit and implicit temperature patterns for text-based fallback search.
    private static readonly Regex NearbyTemperatureRegex = new(
        @"-?\d+(?:\.\d+)?\s*(?:°\s*C|deg(?:rees?)?\s*C)\b"
        + @"|\broom\s*temp(?:erature)?\b|\bambient\s*temp(?:erature)?\b"
        + @"|\bice[\s-](?:bath|water\s+bath)\b|\breflux\b"
        + @"|\b[Rr]\.?[Tt]\.?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int NearbyCharWindow = 250;

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

        Match impliedMatch = ImpliedTempRegex.Match(text);
        if (!impliedMatch.Success)
        {
            return findings;
        }

        // Check for any temperature claim: numeric (contextKey "temp") or symbolic
        bool hasTempClaim = claims.Any(c =>
            c.ClaimType == ClaimType.SymbolicTemperature
            || (c.JsonPayload is not null
                && c.JsonPayload.Contains("\"temp\"", StringComparison.OrdinalIgnoreCase)));

        if (hasTempClaim)
        {
            return findings;
        }

        // Fallback: scan raw text near the implied-temp match for temperature patterns.
        // This catches cases where the extractor missed a temperature that is clearly
        // present in the text (e.g., "cooled to 10°C" where "cooled to" triggers but
        // "10°C" was not extracted as a claim).
        int searchStart = Math.Max(0, impliedMatch.Index - NearbyCharWindow);
        int searchEnd = Math.Min(text.Length, impliedMatch.Index + impliedMatch.Length + NearbyCharWindow);
        string window = text[searchStart..searchEnd];

        Match nearbyTemp = NearbyTemperatureRegex.Match(window);
        if (nearbyTemp.Success)
        {
            return findings;
        }

        findings.Add(new ValidationFinding
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ValidatorName = nameof(MissingTemperatureWhenImpliedValidator),
            Status = ValidationStatus.Fail,
            Message = $"[CHEM.MISSING_TEMPERATURE] Temperature control is implied (e.g., {impliedMatch.Value}) but no temperature was specified.",
            Confidence = 0.85,
            Kind = FindingKind.MissingTemperature,
            EvidenceRef = $"ImpliedTemp@{impliedMatch.Index}"
        });

        return findings;
    }
}
