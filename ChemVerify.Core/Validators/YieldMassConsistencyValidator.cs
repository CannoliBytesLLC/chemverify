using System.Globalization;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

/// <summary>
/// When a product mass (mg/g), a yield percentage, and a starting-material
/// mass or mmol are all present, checks whether the reported product mass
/// is consistent with the stated yield. If insufficient context exists to
/// perform the check, silently skips (no false positive).
/// </summary>
public class YieldMassConsistencyValidator : IValidator
{
    private const double ToleranceFraction = 0.35; // 35% tolerance — generous for MW uncertainty
    private const double AbsoluteBufferMg = 5.0;   // absolute buffer in mg to avoid razor-edge false fails

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();

        // Yield claim (percent context)
        ExtractedClaim? yieldClaim = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit
                     && c.Unit == "%"
                     && c.JsonPayload is not null
                     && c.JsonPayload.Contains("\"yield\"", StringComparison.OrdinalIgnoreCase)
                     && c.NormalizedValue is not null)
            .OrderByDescending(c => c.StepIndex ?? 0)
            .FirstOrDefault();

        if (yieldClaim is null) return findings;

        if (!double.TryParse(yieldClaim.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double yieldPct)
            || yieldPct <= 0 || yieldPct > 100)
            return findings;

        // Find mass claims: first is starting material, last before/at yield step is product
        List<ExtractedClaim> massClaims = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit
                     && c.Unit is "mg" or "g"
                     && c.NormalizedValue is not null)
            .OrderBy(c => c.StepIndex ?? 0)
            .ThenBy(c => ParseStartOffset(c.SourceLocator))
            .ToList();

        if (massClaims.Count < 2) return findings;

        ExtractedClaim startingMass = massClaims[0];
        ExtractedClaim productMass = massClaims[^1];

        // They should be in different steps or at least different positions
        if (startingMass.Id == productMass.Id) return findings;

        if (!double.TryParse(startingMass.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double startVal)
            || startVal <= 0)
            return findings;

        if (!double.TryParse(productMass.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double prodVal)
            || prodVal <= 0)
            return findings;

        // Normalize to same unit
        double startMg = startingMass.Unit == "g" ? startVal * 1000 : startVal;
        double prodMg = productMass.Unit == "g" ? prodVal * 1000 : prodVal;

        // Implied yield from masses (assuming MW conservation — only approximate)
        double impliedYieldPct = (prodMg / startMg) * 100.0;

        // Compare: the stated yield% and the mass-ratio yield% should be in the same ballpark.
        // For a single-step reaction with similar starting/product MW, yield ≈ prodMass/startMass.
        // For multi-step or different MWs this is only a sanity check.
        // An absolute buffer (AbsoluteBufferMg) avoids razor-edge false fails on small masses.
        double diff = Math.Abs(impliedYieldPct - yieldPct);
        double relError = diff / Math.Max(yieldPct, 1.0);
        double toleranceCeiling = (1.0 + ToleranceFraction) * 100.0;
        double bufferAdjustment = (AbsoluteBufferMg / Math.Max(startMg, 0.001)) * 100.0;

        if (relError > ToleranceFraction && impliedYieldPct > toleranceCeiling + bufferAdjustment)
        {
            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimId = yieldClaim.Id,
                ValidatorName = nameof(YieldMassConsistencyValidator),
                Status = ValidationStatus.Fail,
                Message = $"[CHEM.YIELD_MASS_INCONSISTENT] Stated {yieldPct}% yield, but product mass ({productMass.RawText}) vs starting mass ({startingMass.RawText}) implies ~{impliedYieldPct:F0}% mass recovery — may indicate an error.",
                Confidence = 0.6,
                Kind = FindingKind.YieldMassInconsistent,
                JsonPayload = $"{{\"statedYield\":{yieldPct},\"massRecoveryPct\":{impliedYieldPct:F1},\"startingMassMg\":{startMg},\"productMassMg\":{prodMg}}}"
            });
        }

        return findings;
    }

    private static int ParseStartOffset(string? locator)
    {
        if (locator is null || !locator.StartsWith("AnalyzedText:"))
            return int.MaxValue;

        string[] parts = locator["AnalyzedText:".Length..].Split('-');
        return parts.Length >= 1 && int.TryParse(parts[0], out int start) ? start : int.MaxValue;
    }
}
