using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Governance;
using ChemVerify.Core.Procedure;

namespace ChemVerify.Core.Governance.Suppressors;

/// <summary>
/// Suppresses dryness/aqueous contradictions and ambiguous workup transitions
/// once the procedure has explicitly entered a Quench/Extraction/Workup phase.
/// Aqueous media in those phases is the *expected* regime, not a contradiction
/// with the dry/inert reaction phase.
/// </summary>
public sealed class DrynessPostQuenchSuppressor : IFindingSuppressor
{
    public string Id => "SUPPRESS_DRYNESS_POST_QUENCH";

    public ValidationOutcomeAdjustment Evaluate(ValidatorDecisionContext context)
    {
        if (context.Finding.Kind is not (
                FindingKind.DrynessSemanticContradiction
                or FindingKind.AmbiguousWorkupTransition
                or FindingKind.AtmosphereContainmentIssue))
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
        bool postQuench = after.CurrentPhase >= ReactionPhase.Quench
                       || after.CurrentFlags.Contains(ProcedureStateFlags.PostQuench);

        if (!postQuench)
        {
            return ValidationOutcomeAdjustment.None;
        }

        // AmbiguousWorkupTransition is fully expected once explicit workup language has occurred.
        if (context.Finding.Kind == FindingKind.AmbiguousWorkupTransition)
        {
            return ValidationOutcomeAdjustment.Suppressed(
                SuppressionReason.WorkupTransitionExpected,
                "Procedure has entered the Quench/Workup phase; aqueous media is expected and "
                + "does not indicate an ambiguous transition.");
        }

        // Dryness/atmosphere contradictions during a workup are downgraded
        // rather than fully suppressed: drying language can still be wrong
        // (e.g., "dried with water"), so we keep visibility but reduce noise.
        return ValidationOutcomeAdjustment.Downgrade(
            multiplier: 0.5,
            SuppressionReason.PostQuenchAqueousExpected,
            "Finding occurs after the Quench/Workup phase boundary where aqueous handling is "
            + "the expected regime; downgrading confidence.");
    }
}
