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
/// </summary>
public class DryInertMismatchValidator : IValidator
{
    private static readonly Regex AqueousMediaRegex = new(
        @"\b(water|H2O|brine|aqueous|sat\w*\s+(?:NaCl|NH4Cl|NaHCO3))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WorkupTransitionRegex = new(
        @"\b(quench(?:ed|ing)?|work[- ]?up|workup|extract(?:ed|ion)|wash(?:ed|ing)?|"
        + @"partition(?:ed)?|separate(?:d|ing)?|organic\s+layer|aqueous\s+layer|"
        + @"pour(?:ed)?\s+(?:into|onto))\b",
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

        // Look for aqueous media in later steps
        foreach (TextStep step in steps)
        {
            if (step.Index <= minDryStep) continue;

            string stepText = text[step.StartOffset..step.EndOffset];

            if (!AqueousMediaRegex.IsMatch(stepText)) continue;

            // Aqueous found — check if workup transition language is also present
            if (WorkupTransitionRegex.IsMatch(stepText)) continue;

            // Also check the immediately preceding step for workup language
            TextStep? prevStep = steps.FirstOrDefault(s => s.Index == step.Index - 1);
            if (prevStep is { } ps)
            {
                string prevText = text[ps.StartOffset..ps.EndOffset];
                if (WorkupTransitionRegex.IsMatch(prevText)) continue;
            }

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
