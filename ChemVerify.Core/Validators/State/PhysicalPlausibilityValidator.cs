using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators.State;

/// <summary>
/// Sanity-checks individual numeric claims against physical bounds:
/// negative masses/volumes/times, and molecular weights outside the
/// 1 – 5000 g/mol range commonly seen in small-molecule synthesis.
/// </summary>
public sealed class PhysicalPlausibilityValidator : ValidatorBase
{
    private const double MinMolecularWeight = 1.0;
    private const double MaxMolecularWeight = 5000.0;

    protected override IReadOnlyList<ValidationFinding> ExecuteValidation(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = [];

        foreach (ExtractedClaim claim in claims)
        {
            if (claim.ClaimType != ClaimType.NumericWithUnit) continue;
            if (!double.TryParse(claim.NormalizedValue, out double v)) continue;

            string? unit = claim.Unit;

            // Negative masses / volumes / times / pressures — never physical
            bool nonNegativeUnit = unit is "g" or "mg" or "kg" or "mL" or "L"
                or "mol" or "mmol" or "h" or "min" or "atm" or "kPa" or "ppm" or "M";
            if (nonNegativeUnit && v < 0)
            {
                findings.Add(MakeFinding(runId, claim,
                    $"[CHEM.PHYSICALLY_IMPLAUSIBLE] Negative {unit} value ({v}) is not physical."));
                continue;
            }

            // Molecular weight bounds (g/mol)
            if (unit == "g/mol" && (v < MinMolecularWeight || v > MaxMolecularWeight))
            {
                findings.Add(MakeFinding(runId, claim,
                    $"[CHEM.PHYSICALLY_IMPLAUSIBLE] Molecular weight {v} g/mol is outside plausible range ({MinMolecularWeight}-{MaxMolecularWeight})."));
            }
        }

        return findings;
    }

    private ValidationFinding MakeFinding(Guid runId, ExtractedClaim claim, string message) =>
        BuildFinding(
            runId,
            ValidationStatus.Fail,
            message,
            confidence: 0.95,
            claimId: claim.Id,
            kind: FindingKind.PhysicallyImplausibleValue,
            evidenceRef: FormatClaimRef(claim.Id),
            evidenceSnippet: claim.RawText,
            evidenceStepIndex: claim.StepIndex);
}
