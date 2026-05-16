using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Procedure;

namespace ChemVerify.Core.Validators.State;

/// <summary>
/// Flags procedural ordering anomalies — e.g. concentrating to dryness, then
/// adding a reagent in a step that does not first re-introduce solvent.
/// </summary>
public sealed class ProceduralOrderingValidator : ValidatorBase
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
            // Anomaly: previously concentrated to dryness, this step adds a reagent
            // but does NOT introduce a solvent first (solvent count still 0 after step).
            bool wasDry = snap.BeforeState.IsConcentratedToDryness;
            bool addedReagent = snap.Transitions.Any(t => t.Kind == StateTransitionKind.ReagentAdded);
            bool addedSolvent = snap.Transitions.Any(t => t.Kind == StateTransitionKind.SolventAdded);

            if (wasDry && addedReagent && !addedSolvent && snap.AfterState.CurrentSolvents.Count == 0)
            {
                findings.Add(BuildFinding(
                    runId,
                    ValidationStatus.Fail,
                    $"[CHEM.ORDERING_ANOMALY] Step {snap.StepIndex} adds reagent after concentration to dryness without re-introducing a solvent.",
                    confidence: 0.7,
                    kind: FindingKind.ProceduralOrderingAnomaly,
                    evidenceRef: $"Step:{snap.StepIndex}",
                    evidenceStartOffset: snap.StepStartOffset,
                    evidenceEndOffset: snap.StepEndOffset,
                    evidenceStepIndex: snap.StepIndex,
                    evidenceSnippet: Truncate(snap.StepText.Trim(), 140)));
            }

            // Anomaly: phase moved backwards (purification -> reaction)
            // Allow normal multi-stage cycles:
            //   * Workup -> Extraction (extract aqueous, combine organics)
            //   * Cooling -> Heating (second heating/reflux stage)
            //   * any -> Reaction when a new prep/addition begins (reagent added)
            if (snap.BeforeState.CurrentPhase > snap.AfterState.CurrentPhase
                && snap.AfterState.CurrentPhase >= ReactionPhase.Reaction)
            {
                ReactionPhase from = snap.BeforeState.CurrentPhase;
                ReactionPhase to = snap.AfterState.CurrentPhase;

                bool workupToExtraction = from == ReactionPhase.Workup
                    && to == ReactionPhase.Extraction;
                bool coolingToHeating = from == ReactionPhase.Cooling
                    && to == ReactionPhase.Heating;
                bool reagentAdditionRestart = to == ReactionPhase.Reaction
                    && snap.Transitions.Any(t => t.Kind == StateTransitionKind.ReagentAdded);
                bool analysisHere = snap.AfterState.CurrentFlags.Contains(
                    ProcedureStateFlags.AnalyticalContext);

                if (workupToExtraction || coolingToHeating || reagentAdditionRestart
                    || analysisHere)
                {
                    continue;
                }

                findings.Add(BuildFinding(
                    runId,
                    ValidationStatus.Fail,
                    $"[CHEM.ORDERING_ANOMALY] Step {snap.StepIndex} regresses procedure phase from {from} back to {to}.",
                    confidence: 0.6,
                    kind: FindingKind.ProceduralOrderingAnomaly,
                    evidenceRef: $"Step:{snap.StepIndex}",
                    evidenceStartOffset: snap.StepStartOffset,
                    evidenceEndOffset: snap.StepEndOffset,
                    evidenceStepIndex: snap.StepIndex,
                    evidenceSnippet: Truncate(snap.StepText.Trim(), 140)));
            }
        }

        return findings;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
