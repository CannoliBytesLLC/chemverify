using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Procedure;

namespace ChemVerify.Core.Validators.State;

/// <summary>
/// Detects semantic contradictions between drying language and the medium
/// referenced in the same step — e.g. "dried with water", "anhydrous water",
/// "dried over brine".
/// </summary>
public sealed class DrynessConsistencyValidator : ValidatorBase
{
    private static readonly Regex DryingWithAqueousRegex = new(
        @"\b(?:dried?|anhydrous|drying)\s+(?:over|with|using|in)\s+(?:de[\s-]?ionized\s+)?(?:water|H2O|brine|aqueous\s+\w+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    protected override IReadOnlyList<ValidationFinding> ExecuteValidation(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = [];
        string text = run.GetAnalyzedText();
        if (string.IsNullOrEmpty(text)) return findings;

        IReadOnlyList<StateSnapshot> snapshots = ProcedureStateEngine.Build(text, claims);

        foreach (StateSnapshot snap in snapshots)
        {
            Match m = DryingWithAqueousRegex.Match(snap.StepText);
            if (!m.Success) continue;

            findings.Add(BuildFinding(
                runId,
                ValidationStatus.Fail,
                $"[CHEM.DRYNESS_CONTRADICTION] Step {snap.StepIndex} uses drying language with an aqueous medium: \"{m.Value.Trim()}\".",
                confidence: 0.9,
                kind: FindingKind.DrynessSemanticContradiction,
                evidenceRef: $"Step:{snap.StepIndex}",
                evidenceStartOffset: snap.StepStartOffset + m.Index,
                evidenceEndOffset: snap.StepStartOffset + m.Index + m.Length,
                evidenceStepIndex: snap.StepIndex,
                evidenceSnippet: m.Value.Trim()));
        }

        return findings;
    }
}
