using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Chemistry;
using ChemVerify.Core.Procedure;

namespace ChemVerify.Core.Validators.State;

/// <summary>
/// Detects implausible solvent / temperature combinations:
/// <list type="bullet">
///   <item>A numeric temperature exceeds the active solvent's boiling point in an open system.</item>
///   <item>"Reflux" is specified at a temperature inconsistent with the active solvent's BP.</item>
/// </list>
/// </summary>
public sealed class SolventTemperatureValidator : ValidatorBase
{
    private const double RefluxTolerance = 8.0;     // °C
    private const double BoilingExceedTolerance = 3.0;

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
            ProcedureState s = snap.AfterState;
            if (s.CurrentSolvents.Count == 0) continue;

            // Skip steps whose temperature is a measured product property
            // (mp, bp at reduced pressure, m/e, NMR/MS) rather than a
            // reaction condition. These are tagged by the state engine.
            bool analytical = s.CurrentFlags.Contains(ProcedureStateFlags.AnalyticalContext)
                || s.CurrentFlags.Contains(ProcedureStateFlags.ProductProperty)
                || s.CurrentFlags.Contains(ProcedureStateFlags.VacuumDistillation);
            if (analytical) continue;

            (string solvent, double bp)? lowest = LowestBoilingSolvent(s.CurrentSolvents);
            if (lowest is null) continue;
            (string solvent, double bp) low = lowest.Value;

            // Case 1: numeric temp exceeds BP without sealed/inert/refluxing system.
            // Inert-atmosphere work (Schlenk, sealed flask under N2/Ar) and
            // explicit reflux setups are closed apparatus by convention, so
            // we don't flag them here — those scenarios are checked separately.
            bool closedApparatus = s.IsSealedSystem
                || s.IsUnderInertAtmosphere
                || s.CurrentSymbolicTemperature == "reflux"
                || s.CurrentFlags.Contains(ProcedureStateFlags.Refluxing);

            if (s.CurrentTemperatureCelsius is double t
                && !closedApparatus
                && t > low.bp + BoilingExceedTolerance)
            {
                findings.Add(BuildFinding(
                    runId,
                    ValidationStatus.Fail,
                    $"[CHEM.SOLVENT_TEMP_IMPLAUSIBLE] Temperature {t:0.#} °C exceeds boiling point of {low.solvent} ({low.bp:0.#} °C) without sealed/pressurized system.",
                    confidence: 0.85,
                    kind: FindingKind.SolventTemperatureImplausible,
                    evidenceRef: $"Step:{snap.StepIndex}",
                    evidenceStartOffset: snap.StepStartOffset,
                    evidenceEndOffset: snap.StepEndOffset,
                    evidenceStepIndex: snap.StepIndex,
                    evidenceSnippet: Truncate(snap.StepText.Trim(), 140)));
                continue;
            }

            // Case 2: "reflux" requested but numeric temp is far below the lowest BP
            bool refluxing = s.CurrentSymbolicTemperature == "reflux"
                || s.CurrentFlags.Contains("refluxing");

            if (refluxing
                && s.CurrentTemperatureCelsius is double rt
                && rt + RefluxTolerance < low.bp)
            {
                findings.Add(BuildFinding(
                    runId,
                    ValidationStatus.Fail,
                    $"[CHEM.IMPOSSIBLE_REFLUX] Reflux specified at {rt:0.#} °C in {low.solvent} (BP {low.bp:0.#} °C) — temperature is below boiling point.",
                    confidence: 0.85,
                    kind: FindingKind.ImpossibleRefluxCondition,
                    evidenceRef: $"Step:{snap.StepIndex}",
                    evidenceStartOffset: snap.StepStartOffset,
                    evidenceEndOffset: snap.StepEndOffset,
                    evidenceStepIndex: snap.StepIndex,
                    evidenceSnippet: Truncate(snap.StepText.Trim(), 140)));
            }
        }

        return findings;
    }

    private static (string Solvent, double Bp)? LowestBoilingSolvent(IReadOnlyList<string> solvents)
    {
        (string Solvent, double Bp)? best = null;
        foreach (string s in solvents)
        {
            double? bp = ChemistryReference.GetBoilingPointCelsius(s);
            if (bp is null) continue;
            if (best is null || bp.Value < best.Value.Bp)
                best = (s, bp.Value);
        }
        return best;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
