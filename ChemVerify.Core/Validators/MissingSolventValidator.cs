using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MissingSolventValidator : IValidator
{
    private static readonly Regex ProcedureVerbRegex = new(
        @"\b(dissolve[ds]?|stirr?(ed|ing)?|reflux(ed|ing)?|heat(ed|ing)?|cool(ed|ing)?|add(ed|ing)?|quench(ed|ing)?|extract(ed|ing)?|wash(ed|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SolventRegex = new(
        @"\b(THF|tetrahydrofuran|ether|diethyl\s+ether|DCM|dichloromethane|toluene|hexane|pentane|EtOAc|ethyl\s+acetate|MeCN|acetonitrile|DMF|DMSO|MeOH|methanol|EtOH|ethanol|water|aqueous|chloroform|CHCl3|acetone|benzene|xylene|dioxane)\b",
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

        bool hasProcedureVerbs = ProcedureVerbRegex.IsMatch(text);
        bool hasSolvent = SolventRegex.IsMatch(text);

        if (hasProcedureVerbs && !hasSolvent)
        {
            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = nameof(MissingSolventValidator),
                Status = ValidationStatus.Fail,
                Message = "[CHEM.MISSING_SOLVENT] Procedure includes reaction steps but no solvent/medium is specified.",
                Confidence = 0.8,
                Kind = FindingKind.MissingSolvent
            });
        }

        return findings;
    }
}
