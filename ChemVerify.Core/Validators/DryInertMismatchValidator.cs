using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Core.Validators;

/// <summary>
/// If early steps establish dry/inert conditions (anhydrous, N2, argon, etc.)
/// and later steps introduce aqueous/protic media (water, brine, H2O) without
/// explicit workup/quench/extraction language, emit a
/// <see cref="FindingKind.AmbiguousWorkupTransition"/> warning.
/// Only Procedure-classified steps are checked for workup transitions and
/// aqueous media to avoid narrative contamination.
/// </summary>
public class DryInertMismatchValidator : IValidator
{
    private static readonly Regex AqueousMediaRegex = new(
        @"\b(water|H2O|brine|aqueous|sat\w*\s+(?:NaCl|NH4Cl|NaHCO3))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WorkupTransitionRegex = new(
        @"\b(quench(?:ed|ing)?|work[- ]?up|workup|extract(?:ed|ion)|wash(?:ed|ing)?|"
        + @"partition(?:ed)?|separate(?:d|ing)?|organic\s+layer|aqueous\s+layer|"
        + @"pour(?:ed)?\s+(?:into|onto)|"
        + @"added?\s+(?:to\s+)?(?:ice|water|sat\w*\s+NH4Cl|sat\w*\s+NaHCO3|brine)|"
        + @"neutrali[sz](?:ed|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();
        if (string.IsNullOrEmpty(text)) return findings;

        // Find dry/inert conditions
        List<ExtractedClaim> dryInertClaims = claims
            .Where(c => c.ClaimType is ClaimType.DrynessCondition or ClaimType.AtmosphereCondition
                     && c.NormalizedValue is not "air")
            .ToList();

        if (dryInertClaims.Count == 0) return findings;

        int minDryStep = dryInertClaims.Min(c => c.StepIndex ?? 0);

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        // Classify step roles — only scan Procedure steps for aqueous media and
        // workup transitions to avoid narrative contamination (e.g. "added to water"
        // in a literature discussion).
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, ctx.ReferencesStartOffset);

        // Look for aqueous media in later Procedure steps
        foreach (TextStep step in steps)
        {
            if (step.Index <= minDryStep) continue;
            if (!roles.TryGetValue(step.Index, out StepRole role) || role != StepRole.Procedure)
                continue;

            string stepText = text[step.StartOffset..step.EndOffset];

            if (!AqueousMediaRegex.IsMatch(stepText)) continue;

            // Aqueous found — check if workup transition language is present in
            // this step OR any intermediate Procedure step between the dry/inert
            // condition and this step.
            bool workupFound = WorkupTransitionRegex.IsMatch(stepText);

            if (!workupFound)
            {
                foreach (TextStep intermediateStep in steps)
                {
                    if (intermediateStep.Index <= minDryStep) continue;
                    if (intermediateStep.Index >= step.Index) break;
                    if (!roles.TryGetValue(intermediateStep.Index, out StepRole intRole) || intRole != StepRole.Procedure)
                        continue;

                    string intermediateText = text[intermediateStep.StartOffset..intermediateStep.EndOffset];
                    if (WorkupTransitionRegex.IsMatch(intermediateText))
                    {
                        workupFound = true;
                        break;
                    }
                }
            }

            if (workupFound) continue;

            string dryTokens = string.Join(", ", dryInertClaims.Select(c => c.RawText).Distinct());
            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = nameof(DryInertMismatchValidator),
                Status = ValidationStatus.Fail,
                Message = $"[CHEM.AMBIGUOUS_WORKUP_TRANSITION] Dry/inert conditions ({dryTokens}) established in step {minDryStep}, but aqueous media introduced in step {step.Index} without explicit workup transition.",
                Confidence = 0.7,
                Kind = FindingKind.AmbiguousWorkupTransition,
                EvidenceRef = $"DryStep:{minDryStep}|AqueousStep:{step.Index}",
                JsonPayload = $"{{\"dryTokens\":\"{EscapeJson(dryTokens)}\",\"dryStep\":{minDryStep},\"aqueousStep\":{step.Index}}}"
            });
            break; // One finding per document is sufficient
        }

        return findings;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
