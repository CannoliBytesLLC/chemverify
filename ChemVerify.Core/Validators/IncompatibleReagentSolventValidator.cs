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

        // Only match within steps classified as Procedure
        Match? reagentMatch = null;
        Match? proticMatch = null;

        foreach (TextStep step in steps)
        {
            if (!roles.TryGetValue(step.Index, out StepRole role) || role != StepRole.Procedure)
                continue;

            string stepText = text[step.StartOffset..step.EndOffset];

            reagentMatch ??= MoistureSensitiveRegex.Match(stepText) is { Success: true } rm
                ? rm : null;
            proticMatch ??= ProticMediaRegex.Match(stepText) is { Success: true } pm
                ? pm : null;

            if (reagentMatch is not null && proticMatch is not null)
                break;
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
