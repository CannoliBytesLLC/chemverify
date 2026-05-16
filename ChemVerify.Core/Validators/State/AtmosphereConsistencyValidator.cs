using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Procedure;

namespace ChemVerify.Core.Validators.State;

/// <summary>
/// Detects atmosphere containment failures: an inert atmosphere (N2/Ar) was
/// established but a later step opens the vessel to air without an explicit
/// re-blanketing or workup transition.
/// </summary>
public sealed class AtmosphereConsistencyValidator : ValidatorBase
{
    protected override IReadOnlyList<ValidationFinding> ExecuteValidation(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = [];
        string text = run.GetAnalyzedText();
        IReadOnlyList<StateSnapshot> snapshots = ProcedureStateEngine.Build(text, claims);

        foreach (StateSnapshot snap in snapshots)
        {
            // Detect transition from inert -> air via vessel opening
            bool inertBroken = snap.BeforeState.IsUnderInertAtmosphere
                && !snap.AfterState.IsUnderInertAtmosphere
                && snap.AfterState.CurrentAtmosphere == "air";

            if (!inertBroken) continue;

            // Skip if reaction phase moved to workup (workup typically opens to air intentionally).
            if (snap.AfterState.CurrentPhase >= ReactionPhase.Workup) continue;

            string snippet = Truncate(snap.StepText.Trim(), 140);
            findings.Add(BuildFinding(
                runId,
                ValidationStatus.Fail,
                $"[CHEM.ATMOSPHERE_CONTAINMENT] Inert atmosphere ({snap.BeforeState.CurrentAtmosphere}) broken in step {snap.StepIndex} by open vessel/air exposure.",
                confidence: 0.8,
                kind: FindingKind.AtmosphereContainmentIssue,
                evidenceRef: $"Step:{snap.StepIndex}",
                evidenceStartOffset: snap.StepStartOffset,
                evidenceEndOffset: snap.StepEndOffset,
                evidenceStepIndex: snap.StepIndex,
                evidenceSnippet: snippet));
        }

        return findings;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
