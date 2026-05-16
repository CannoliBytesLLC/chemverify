using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Core.Validators;

public class IncompatibleReagentSolventValidator : IValidator
{
    private static readonly Regex MoistureSensitiveRegex = new(
        @"\b(NaH|sodium\s+hydride|LiAlH4|lithium\s+aluminum\s+hydride|LAH|[Gg]rignard|MgBr|MgCl|n-BuLi|t-BuLi|BuLi|organolithium)\b",
        RegexOptions.Compiled);

    private static readonly Regex ProticMediaRegex = new(
        @"\b(water|aqueous|H2O|methanol|ethanol|isopropanol|tert-butanol|alcohol)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Once any of these begin, the moisture-sensitive reagent is consumed and
    // subsequent aqueous/protic mentions describe workup/isolation, not the
    // active reaction medium. Used to terminate reagent lifecycle.
    private static readonly Regex WorkupBoundaryRegex = new(
        @"\b(quench(?:ed|ing)?|work[- ]?up|workup|extract(?:ed|ion|ing)?|wash(?:ed|ing)?|"
        + @"partition(?:ed|ing)?|aqueous\s+(?:layer|workup|isolation)|brine)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();

        if (string.IsNullOrEmpty(text))
        {
            return findings;
        }

        // Segment steps and classify roles so we only check procedural text
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, ctx.ReferencesStartOffset);

        Match? reagentMatch = null;
        Match? proticMatch = null;
        int? reagentStepIndex = null;
        int? workupStepIndex = null;

        foreach (TextStep step in steps)
        {
            if (!roles.TryGetValue(step.Index, out StepRole role) || role != StepRole.Procedure)
                continue;

            string stepText = text[step.StartOffset..step.EndOffset];

            if (workupStepIndex is null && WorkupBoundaryRegex.IsMatch(stepText))
            {
                workupStepIndex = step.Index;
            }

            if (reagentMatch is null && MoistureSensitiveRegex.Match(stepText) is { Success: true } rm)
            {
                reagentMatch = rm;
                reagentStepIndex = step.Index;
            }

            // Only consider protic matches that occur *before* the reagent's
            // lifecycle terminates (i.e. before any workup/quench/extraction
            // step). NaH followed by aqueous quench is normal procedure, not
            // an incompatibility.
            if (proticMatch is null
                && (workupStepIndex is null || step.Index <= workupStepIndex)
                && ProticMediaRegex.Match(stepText) is { Success: true } pm)
            {
                proticMatch = pm;
            }

            if (reagentMatch is not null && proticMatch is not null)
                break;
        }

        // Suppress when the protic mention only appears after a workup
        // boundary that itself follows the reagent (post-quench aqueous wash).
        if (reagentMatch is not null && proticMatch is not null
            && workupStepIndex is int wIdx && reagentStepIndex is int rIdx
            && wIdx > rIdx)
        {
            // Re-scan: was the protic match strictly inside or after the workup step?
            int proticAbsOffset = proticMatch.Index;
            int workupStepStart = steps.First(s => s.Index == wIdx).StartOffset;
            if (proticAbsOffset >= workupStepStart)
            {
                proticMatch = null;
            }
        }

        if (reagentMatch is not null && proticMatch is not null)
        {
            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = nameof(IncompatibleReagentSolventValidator),
                Status = ValidationStatus.Fail,
                Message = $"[CHEM.INCOMPATIBLE_REAGENT_SOLVENT] Moisture-sensitive reagent ({reagentMatch.Value}) appears in aqueous/protic conditions ({proticMatch.Value}).",
                Confidence = 0.9,
                Kind = FindingKind.IncompatibleReagentSolvent,
                EvidenceRef = $"Reagent@{reagentMatch.Index}+Solvent@{proticMatch.Index}"
            });
        }

        return findings;
    }
}
