using System.Globalization;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

/// <summary>
/// When an entity has both a mass claim (g or mg) and a mmol claim nearby,
/// computes the implied molecular weight (MW = mass / mmol) and checks
/// whether it falls within a chemically plausible range (roughly 10–3000 g/mol).
/// Emits a Pass confirmation when consistent and a warning when implausible.
/// Only pairs mass and mmol claims that share the same entity key to avoid
/// cross-entity mismatches in dense paragraphs.
/// </summary>
public class MwConsistencyValidator : IValidator
{
    private const double MinPlausibleMw = 5.0;     // very small inorganics (LiH ~8, NaH ~24)
    private const double MaxPlausibleMw = 3000.0;   // large peptides / organometallics
    private const int ProximityCharLimit = 100;     // max char distance to pair mass ↔ mmol (entity-key path)
    private const int FallbackProximityLimit = 30;   // tight window for fallback — only pairs within same parenthetical group

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();

        List<ExtractedClaim> massClaims = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit
                     && c.Unit is "g" or "mg"
                     && c.NormalizedValue is not null)
            .ToList();

        List<ExtractedClaim> mmolClaims = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit
                     && c.Unit == "mmol"
                     && c.NormalizedValue is not null)
            .ToList();

        if (massClaims.Count == 0 || mmolClaims.Count == 0)
            return findings;

        foreach (ExtractedClaim massClaim in massClaims)
        {
            // Find a mmol claim from the same step or nearby char offset
            ExtractedClaim? paired = FindPairedMmolClaim(massClaim, mmolClaims);
            if (paired is null) continue;

            if (!double.TryParse(massClaim.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double massVal)
                || massVal <= 0)
                continue;

            if (!double.TryParse(paired.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double mmolVal)
                || mmolVal <= 0)
                continue;

            // Convert mg → g for MW calc
            double massInGrams = massClaim.Unit == "mg" ? massVal / 1000.0 : massVal;
            double impliedMw = massInGrams / (mmolVal / 1000.0); // mmol → mol

            string entity = massClaim.EntityKey ?? massClaim.RawText;

            if (impliedMw >= MinPlausibleMw && impliedMw <= MaxPlausibleMw)
            {
                findings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ClaimId = massClaim.Id,
                    ValidatorName = nameof(MwConsistencyValidator),
                    Status = ValidationStatus.Pass,
                    Message = $"[CHEM.MW_CONSISTENT] {entity}: {massClaim.RawText} / {paired.RawText} → implied MW {impliedMw:F1} g/mol (plausible).",
                    Confidence = 0.75,
                    Kind = FindingKind.MwConsistent,
                    JsonPayload = $"{{\"entity\":\"{EscapeJson(entity)}\",\"massG\":{massInGrams:F4},\"mmol\":{mmolVal},\"impliedMw\":{impliedMw:F1}}}"
                });
            }
            else
            {
                findings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ClaimId = massClaim.Id,
                    ValidatorName = nameof(MwConsistencyValidator),
                    Status = ValidationStatus.Fail,
                    Message = $"[CHEM.MW_IMPLAUSIBLE] {entity}: {massClaim.RawText} / {paired.RawText} → implied MW {impliedMw:F1} g/mol (outside {MinPlausibleMw}–{MaxPlausibleMw} range).",
                    Confidence = 0.7,
                    Kind = FindingKind.MwImplausible,
                    JsonPayload = $"{{\"entity\":\"{EscapeJson(entity)}\",\"massG\":{massInGrams:F4},\"mmol\":{mmolVal},\"impliedMw\":{impliedMw:F1}}}"
                });
            }
        }

        return findings;
    }

    private static ExtractedClaim? FindPairedMmolClaim(
        ExtractedClaim massClaim,
        List<ExtractedClaim> mmolClaims)
    {
        // ── Primary path: entity-key-matched pairing ──────────────────
        if (massClaim.EntityKey is not null)
        {
            List<ExtractedClaim> sameEntity = mmolClaims
                .Where(c => c.EntityKey is not null
                         && string.Equals(c.EntityKey, massClaim.EntityKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Prefer same step first
            ExtractedClaim? sameStep = sameEntity
                .Where(c => c.StepIndex.HasValue
                         && massClaim.StepIndex.HasValue
                         && c.StepIndex == massClaim.StepIndex)
                .MinBy(c => CharDistance(massClaim, c));

            if (sameStep is not null) return sameStep;

            // Fall back to proximity search within same entity
            ExtractedClaim? byProximity = sameEntity
                .Where(c => CharDistance(massClaim, c) < ProximityCharLimit)
                .MinBy(c => CharDistance(massClaim, c));

            if (byProximity is not null) return byProximity;
        }

        // ── Fallback path: proximity-only for null entity keys ────────
        // When entity keys are missing (short texts, generic names), allow
        // pairing if exactly one mmol claim is close-by within the same step
        // and no second mmol candidate exists within the window.
        // Only applies when BOTH mass and mmol claims have null entity keys —
        // if either has a key, we rely on the primary entity-matched path.
        if (massClaim.EntityKey is null && massClaim.StepIndex.HasValue)
        {
            List<ExtractedClaim> nearby = mmolClaims
                .Where(c => c.EntityKey is null
                         && c.StepIndex.HasValue
                         && c.StepIndex == massClaim.StepIndex
                         && CharDistance(massClaim, c) <= FallbackProximityLimit)
                .OrderBy(c => CharDistance(massClaim, c))
                .ToList();

            if (nearby.Count == 1)
                return nearby[0];
        }

        return null;
    }

    private static int CharDistance(ExtractedClaim a, ExtractedClaim b)
    {
        int posA = ParseStartOffset(a.SourceLocator);
        int posB = ParseStartOffset(b.SourceLocator);
        return Math.Abs(posA - posB);
    }

    private static int ParseStartOffset(string? locator)
    {
        if (locator is null || !locator.StartsWith("AnalyzedText:"))
            return int.MaxValue;

        string[] parts = locator["AnalyzedText:".Length..].Split('-');
        return parts.Length >= 1 && int.TryParse(parts[0], out int start) ? start : int.MaxValue;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
