using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MissingTemperatureWhenImpliedValidator : IValidator
{
    // Strong implied-temp cues (always require an explicit temperature).
    private static readonly Regex ImpliedTempRegex = new(
        @"\b(exotherm(ic)?|cooling\s+bath|cryogenic|"
        + @"heated\s+to|cooled\s+to|warmed\s+to|kept\s+at\s+(?!rt\b|room|ambient)|"
        + @"stirred\s+at\s+(?!rt\b|room|ambient))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "dropwise" is a *weak* cue. It only implies temperature control when
    // accompanied by a cooling/cryogenic/exotherm marker, an explicit
    // "maintain below"/"control exotherm"/"carefully" qualifier, or a
    // reactive reagent class (NaH/LiAlH4/BuLi/Grignard/acyl chloride/etc).
    private static readonly Regex DropwiseRegex = new(
        @"\bdropwise\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DropwiseQualifierRegex = new(
        @"\b(ice[\s-]?bath|cooling\s+bath|cryogenic|exotherm(?:ic)?|"
        + @"maintain(?:ed|ing)?\s+below|keep(?:ing)?\s+below|kept\s+below|"
        + @"control(?:led|ling)?\s+exotherm|carefully|"
        + @"-\s*\d+\s*°?\s*C|-78|0\s*°?\s*C|"
        + @"NaH|sodium\s+hydride|LiAlH4|LAH|NaBH4|sodium\s+borohydride|borohydride|"
        + @"n-?BuLi|t-?BuLi|s-?BuLi|"
        + @"Grignard|acyl\s+chloride|acid\s+chloride|chlorosulfon\w+|POCl3|SOCl2|TfOH|oleum)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int DropwiseQualifierWindow = 120;

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
            // Try dropwise gated by nearby cooling/exotherm/reactive-reagent cues.
            Match dropwise = DropwiseRegex.Match(text);
            while (dropwise.Success)
            {
                int wStart = Math.Max(0, dropwise.Index - DropwiseQualifierWindow);
                int wEnd = Math.Min(text.Length, dropwise.Index + dropwise.Length + DropwiseQualifierWindow);
                if (DropwiseQualifierRegex.IsMatch(text[wStart..wEnd]))
                {
                    impliedMatch = dropwise;
                    break;
                }
                dropwise = dropwise.NextMatch();
            }
            if (!impliedMatch.Success)
            {
                return findings;
            }
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
