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
        @"\b(THF|tetrahydrofuran|ether|diethyl\s+ether|Et2O|MTBE|"
        + @"DCM|CH2Cl2|dichloromethane|methylene\s+chloride|"
        + @"toluene|hexane|hexanes|pentane|heptane|cyclohexane|"
        + @"EtOAc|ethyl\s+acetate|"
        + @"MeCN|CH3CN|acetonitrile|"
        + @"DMF|dimethylformamide|DMSO|dimethyl\s+sulfoxide|NMP|"
        + @"MeOH|methanol|EtOH|ethanol|iPrOH|isopropanol|[nt]-?BuOH|"
        + @"water|H2O|aqueous|brine|"
        + @"chloroform|CHCl3|"
        + @"acetone|benzene|xylene|xylenes|dioxane|1,4-dioxane|"
        + @"DME|dimethoxyethane|diglyme|"
        + @"pyridine|carbon\s+tetrachloride|CCl4|"
        + @"[Mm]ethyl\s+ethyl\s+ketone|MEK|2-butanone|"
        + @"petroleum\s+ether|ligroin|"
        + @"[Dd]ichloroethane|DCE|"
        + @"[Dd]imethylacetamide|DMA|DMAc)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Contextual solvent evidence: "dissolved in X", "in X (N mL)", "mixture of X and Y"
    private static readonly Regex SolventContextRegex = new(
        @"\b(dissolved?\s+in|in\s+\d+\s*m[lL]|mixture\s+of)\b",
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

        // Normalize common OCR confusions before checking for solvents
        string normalized = NormalizeOcrTokens(text);

        bool hasProcedureVerbs = ProcedureVerbRegex.IsMatch(text);
        bool hasSolvent = SolventRegex.IsMatch(normalized)
                       || SolventContextRegex.IsMatch(normalized)
                       || claims.Any(c => c.ClaimType == ClaimType.SolventMention);

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

    /// <summary>
    /// Fixes common OCR/encoding confusions where uppercase I (I) appears instead
    /// of lowercase l (l) in chemical formulae: CHCI3 → CHCl3, HCI → HCl, etc.
    /// </summary>
    internal static string NormalizeOcrTokens(string text)
    {
        // Pattern: a letter/digit followed by 'CI' then a digit or word boundary
        // This catches CHCI3, CHCI₃, HCI, CHCI, CH2CI2, etc.
        return Regex.Replace(text, @"(?<=[A-Za-z0-9])CI(?=\d|\b|₃|₂)", "Cl");
    }
}
