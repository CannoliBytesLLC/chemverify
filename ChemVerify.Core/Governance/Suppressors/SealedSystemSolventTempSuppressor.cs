using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Governance;
using ChemVerify.Core.Procedure;

namespace ChemVerify.Core.Governance.Suppressors;

/// <summary>
/// Downgrades or suppresses <see cref="FindingKind.SolventTemperatureImplausible"/>
/// findings when the active procedural state at the finding's step is a
/// sealed/pressurized system, an inert closed apparatus, or under explicit
/// reflux — all valid contexts in which a solvent's atmospheric BP can be
/// safely exceeded.
/// </summary>
public sealed class SealedSystemSolventTempSuppressor : IFindingSuppressor
{
    public string Id => "SUPPRESS_SEALED_SYSTEM_SOLVENT_TEMP";

    public ValidationOutcomeAdjustment Evaluate(ValidatorDecisionContext context)
    {
        if (context.Finding.Kind != FindingKind.SolventTemperatureImplausible)
        {
            return ValidationOutcomeAdjustment.None;
        }

        if (context.StateBag is not IReadOnlyList<StateSnapshot> snapshots
            || snapshots.Count == 0)
        {
            return ValidationOutcomeAdjustment.None;
        }

        int? stepIndex = context.Finding.EvidenceStepIndex;
        StateSnapshot? snapshot = stepIndex.HasValue
            ? snapshots.FirstOrDefault(s => s.StepIndex == stepIndex.Value)
            : null;

        if (snapshot is null)
        {
            return ValidationOutcomeAdjustment.None;
        }

        ProcedureState after = snapshot.AfterState;

        // Hard-suppress: a real closed/pressurized apparatus is in play.
        bool autoclave = after.CurrentFlags.Contains(ProcedureStateFlags.Autoclave);
        bool microwave = after.CurrentFlags.Contains(ProcedureStateFlags.Microwave);
        bool pressureVessel = after.CurrentFlags.Contains(ProcedureStateFlags.PressureVessel);
        bool elevatedPressure = after.CurrentFlags.Contains(ProcedureStateFlags.ElevatedPressure)
                              || after.CurrentPressureAtm is > 1.05;
        bool inertPressure = after.CurrentFlags.Contains(ProcedureStateFlags.InertPressure);

        if (autoclave || microwave || pressureVessel || elevatedPressure || inertPressure)
        {
            return ValidationOutcomeAdjustment.Suppressed(
                SuppressionReason.SealedOrPressurizedSystem,
                "Closed/pressurized apparatus detected (autoclave, microwave, pressure tube, "
                + "elevated or inert pressure); solvent BP exceedance is expected.");
        }

        if (after.IsSealedSystem)
        {
            return ValidationOutcomeAdjustment.Suppressed(
                SuppressionReason.SealedOrPressurizedSystem,
                "Vessel is sealed at the finding's step; solvent BP exceedance is expected behavior.");
        }

        // Reflux under inert atmosphere with a condenser is also closed-apparatus.
        if (after.IsUnderInertAtmosphere
            && after.CurrentFlags.Contains(ProcedureStateFlags.Refluxing))
        {
            return ValidationOutcomeAdjustment.Suppressed(
                SuppressionReason.ExplicitRefluxContext,
                "Reflux under inert atmosphere with a condenser is a closed-apparatus context; "
                + "suppressing solvent BP exceedance.");
        }

        // Soft-downgrade: oil bath / heating mantle implies a controlled
        // heating apparatus where a condenser is conventionally present even
        // when reflux isn't explicitly stated.
        bool oilBath = after.CurrentFlags.Contains(ProcedureStateFlags.OilBath);
        if (oilBath)
        {
            return ValidationOutcomeAdjustment.Downgrade(
                multiplier: 0.4,
                SuppressionReason.ExplicitRefluxContext,
                "Oil-bath / heating-mantle apparatus implies a condenser is in place; downgrading "
                + "BP exceedance to a procedural ambiguity.");
        }

        if (after.CurrentFlags.Contains(ProcedureStateFlags.Refluxing))
        {
            return ValidationOutcomeAdjustment.Downgrade(
                multiplier: 0.5,
                SuppressionReason.ExplicitRefluxContext,
                "Reflux indicated; partial confidence downgrade pending sealed-apparatus confirmation.");
        }

        return ValidationOutcomeAdjustment.None;
    }
}
