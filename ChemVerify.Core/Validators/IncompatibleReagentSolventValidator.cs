using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

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

        Match reagentMatch = MoistureSensitiveRegex.Match(text);
        Match proticMatch = ProticMediaRegex.Match(text);

        if (reagentMatch.Success && proticMatch.Success)
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
