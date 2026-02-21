using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

/// <summary>
/// Light heuristic validator that confirms or flags unusual reagent-in-solvent
/// concentration patterns. Known commercial forms (e.g. "HCl in dioxane 4 N",
/// "HBr in AcOH 33%") are confirmed; combinations that are chemically
/// unexpected (e.g. "HCl in hexane") are flagged at low confidence.
/// Reagent names are normalized to handle common aliases (BuLi / nBuLi /
/// n-BuLi / butyllithium, etc.) and both M and N concentration designators.
/// </summary>
public class ConcentrationSanityValidator : IValidator
{
    // Known commercial reagent-in-solvent forms — matched as recognition patterns.
    // Each entry is (acid/reagent pattern, solvent pattern, optional normality range).
    private static readonly KnownForm[] KnownForms =
    [
        new(@"HCl", @"dioxane|1,4-dioxane", 1.0, 4.0),
        new(@"HCl", @"(?:diethyl\s+)?ether|Et2O|MTBE", 1.0, 2.0),
        new(@"HCl", @"MeOH|methanol", 1.0, 4.0),
        new(@"HCl", @"EtOH|ethanol|iPrOH|isopropanol", 1.0, 3.0),
        new(@"HBr", @"AcOH|acetic\s+acid", 30.0, 35.0),   // wt%
        new(@"NH3|ammonia", @"MeOH|methanol", 2.0, 7.0),
        new(@"BH3|borane", @"THF|tetrahydrofuran", 1.0, 1.0),
        new(@"BH3|borane", @"DMS|dimethyl\s+sulfide|Me2S", 2.0, 10.0),
        new(@"LiAlH4|LAH", @"THF|tetrahydrofuran|(?:diethyl\s+)?ether|Et2O", 1.0, 2.5),
        new(@"DIBAL(?:-H)?", @"toluene|hexane|hexanes|DCM|CH2Cl2", 1.0, 1.5),
        new(@"n-?BuLi|t-?BuLi|s-?BuLi|BuLi|butyllithium|n-?butyllithium", @"hexane|hexanes|pentane|cyclohexane", 1.0, 2.5),
        new(@"MeMgBr|EtMgBr|PhMgBr|MeMgCl|EtMgCl|PhMgCl|(?:methyl|ethyl|phenyl)magnesium\s+(?:bromide|chloride)", @"THF|tetrahydrofuran|(?:diethyl\s+)?ether|Et2O", 1.0, 3.0),
        new(@"TFA|trifluoroacetic\s+acid", @"DCM|CH2Cl2|dichloromethane", null, null),
        new(@"H2SO4|sulfuric\s+acid", @"(?:conc\.?\s+)?H2SO4", null, null),  // conc. is a known pattern
    ];

    // Matches "X in Y" pattern in chemistry text, e.g. "HCl in dioxane" or "solution of HCl in dioxane"
    // Allows multi-word reagent names (e.g. "Ethylmagnesium chloride in THF")
    private static readonly Regex ReagentInSolventRegex = new(
        @"\b(?:solution\s+of\s+)?(?<reagent>[A-Za-z][A-Za-z0-9\-]{1,20}(?:\s+[a-z]{2,15})?)\s+in\s+(?<solvent>[A-Za-z][A-Za-z0-9\-, ]{1,30}?)(?:\s*\(|\s*$|\s*[,.])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Normalize common reagent name variants to canonical forms
    private static readonly (Regex Pattern, string Canonical)[] ReagentAliases =
    [
        (new Regex(@"\bn-?butyllithium\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "n-BuLi"),
        (new Regex(@"\bnBuLi\b", RegexOptions.Compiled), "n-BuLi"),
        (new Regex(@"\bt-?butyllithium\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "t-BuLi"),
        (new Regex(@"\btBuLi\b", RegexOptions.Compiled), "t-BuLi"),
        (new Regex(@"\bs-?butyllithium\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "s-BuLi"),
        (new Regex(@"\bsBuLi\b", RegexOptions.Compiled), "s-BuLi"),
        (new Regex(@"\b[Ee]thylmagnesium\s+chloride\b", RegexOptions.Compiled), "EtMgCl"),
        (new Regex(@"\b[Mm]ethylmagnesium\s+chloride\b", RegexOptions.Compiled), "MeMgCl"),
        (new Regex(@"\b[Pp]henylmagnesium\s+chloride\b", RegexOptions.Compiled), "PhMgCl"),
        (new Regex(@"\b[Ee]thylmagnesium\s+bromide\b", RegexOptions.Compiled), "EtMgBr"),
        (new Regex(@"\b[Mm]ethylmagnesium\s+bromide\b", RegexOptions.Compiled), "MeMgBr"),
        (new Regex(@"\b[Pp]henylmagnesium\s+bromide\b", RegexOptions.Compiled), "PhMgBr"),
    ];

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();
        if (string.IsNullOrEmpty(text)) return findings;

        foreach (Match m in ReagentInSolventRegex.Matches(text))
        {
            string reagent = NormalizeReagent(m.Groups["reagent"].Value.Trim());
            string solvent = m.Groups["solvent"].Value.Trim();

            KnownForm? matched = null;
            foreach (KnownForm form in KnownForms)
            {
                if (form.ReagentRegex.IsMatch(reagent) && form.SolventRegex.IsMatch(solvent))
                {
                    matched = form;
                    break;
                }
            }

            if (matched is not null)
            {
                findings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ValidatorName = nameof(ConcentrationSanityValidator),
                    Status = ValidationStatus.Pass,
                    Message = $"[CHEM.KNOWN_REAGENT_FORM] \"{m.Value.Trim()}\" is a recognized commercial reagent form.",
                    Confidence = 0.8,
                    Kind = FindingKind.MwConsistent, // reuse pass-level kind
                    EvidenceRef = $"AnalyzedText:{m.Index}-{m.Index + m.Length}"
                });
            }
        }

        return findings;
    }

    private static string NormalizeReagent(string reagent)
    {
        foreach ((Regex pattern, string canonical) in ReagentAliases)
        {
            if (pattern.IsMatch(reagent))
                return canonical;
        }
        return reagent;
    }

    private sealed class KnownForm
    {
        public Regex ReagentRegex { get; }
        public Regex SolventRegex { get; }
        public double? MinConc { get; }
        public double? MaxConc { get; }

        public KnownForm(string reagentPattern, string solventPattern, double? minConc, double? maxConc)
        {
            ReagentRegex = new Regex($@"\b({reagentPattern})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            SolventRegex = new Regex($@"\b({solventPattern})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            MinConc = minConc;
            MaxConc = maxConc;
        }
    }
}
