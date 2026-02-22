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
/// For common reagents with known MW, cross-checks against the expected value.
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
    private const double KnownMwTolerancePercent = 35.0; // max deviation from known MW before flagging

    // Known molecular weights for common reagents (g/mol).
    // Keys are lowercase entity-key forms the extractor produces.
    private static readonly Dictionary<string, double> KnownMolecularWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hcl"] = 36.46,
        ["hydrochloric acid"] = 36.46,
        ["naoh"] = 40.00,
        ["sodium hydroxide"] = 40.00,
        ["koh"] = 56.11,
        ["nahco3"] = 84.01,
        ["na2co3"] = 105.99,
        ["k2co3"] = 138.21,
        ["cs2co3"] = 325.82,
        ["nah"] = 24.00,
        ["sodium hydride"] = 24.00,
        ["nabh4"] = 37.83,
        ["sodium borohydride"] = 37.83,
        ["lialh4"] = 37.95,
        ["lah"] = 37.95,
        ["tea"] = 101.19,
        ["triethylamine"] = 101.19,
        ["et3n"] = 101.19,
        ["dipea"] = 129.24,
        ["dcc"] = 206.33,
        ["dmap"] = 122.17,
        ["pyridine"] = 79.10,
        ["acoh"] = 60.05,
        ["acetic acid"] = 60.05,
        ["tfa"] = 114.02,
        ["h2so4"] = 98.08,
        ["sulfuric acid"] = 98.08,
        ["hno3"] = 63.01,
        ["buli"] = 64.06,
        ["n-buli"] = 64.06,
        ["t-buli"] = 64.06,
        ["s-buli"] = 64.06,
        ["mcpba"] = 172.57,
        ["pdc"] = 332.92,
        ["pcc"] = 215.56,
        ["nh4cl"] = 53.49,
    };

    // Entity keys that commonly refer to hydrochloride salts rather than free HCl
    // when the implied MW is much higher than 36.46
    private static readonly HashSet<string> HydrochlorideSaltCandidates = new(StringComparer.OrdinalIgnoreCase)
    {
        "hcl", "hci", "hydrochloric acid"
    };

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

            // ── Check against known MW for common reagents ──────────
            if (entity is not null && KnownMolecularWeights.TryGetValue(entity, out double knownMw))
            {
                double deviationPercent = Math.Abs(impliedMw - knownMw) / knownMw * 100.0;

                if (deviationPercent <= KnownMwTolerancePercent)
                {
                    findings.Add(new ValidationFinding
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        ClaimId = massClaim.Id,
                        ValidatorName = nameof(MwConsistencyValidator),
                        Status = ValidationStatus.Pass,
                        Message = $"[CHEM.MW_CONSISTENT] {entity}: {massClaim.RawText} / {paired.RawText} → implied MW {impliedMw:F1} g/mol (known MW {knownMw:F1}, within tolerance).",
                        Confidence = 0.9,
                        Kind = FindingKind.MwConsistent,
                        JsonPayload = $"{{\"entity\":\"{EscapeJson(entity)}\",\"massG\":{massInGrams:F4},\"mmol\":{mmolVal},\"impliedMw\":{impliedMw:F1},\"knownMw\":{knownMw:F1}}}"
                    });
                }
                else if (HydrochlorideSaltCandidates.Contains(entity) && impliedMw > knownMw * 2)
                {
                    // Likely a hydrochloride salt (e.g. "HCl" used loosely for amine·HCl)
                    findings.Add(new ValidationFinding
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        ClaimId = massClaim.Id,
                        ValidatorName = nameof(MwConsistencyValidator),
                        Status = ValidationStatus.Unverified,
                        Message = $"[CHEM.MW_ENTITY_AMBIGUOUS] {entity}: implied MW {impliedMw:F1} g/mol ≠ free acid ({knownMw:F1}); likely a hydrochloride salt — verify entity identity.",
                        Confidence = 0.6,
                        Kind = FindingKind.EntityAmbiguous,
                        JsonPayload = $"{{\"entity\":\"{EscapeJson(entity)}\",\"massG\":{massInGrams:F4},\"mmol\":{mmolVal},\"impliedMw\":{impliedMw:F1},\"knownMw\":{knownMw:F1}}}"
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
                        Message = $"[CHEM.MW_KNOWN_MISMATCH] {entity}: implied MW {impliedMw:F1} g/mol vs known MW {knownMw:F1} g/mol ({deviationPercent:F0}% deviation).",
                        Confidence = 0.8,
                        Kind = FindingKind.MwKnownMismatch,
                        JsonPayload = $"{{\"entity\":\"{EscapeJson(entity)}\",\"massG\":{massInGrams:F4},\"mmol\":{mmolVal},\"impliedMw\":{impliedMw:F1},\"knownMw\":{knownMw:F1}}}"
                    });
                }
                continue;
            }

            // ── Plausible-range fallback for unknown entities ────────
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
