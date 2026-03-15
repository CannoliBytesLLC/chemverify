using System.Text.RegularExpressions;
using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Core.Validators;

/// <summary>
/// Classifies reagent names into <see cref="ReactivityTier"/> levels to gate
/// quench/workup validation. Patterns are matched case-insensitively.
/// </summary>
public static partial class ReactivityClassifier
{
    // ── High-reactivity: organolithiums, Grignards, metal hydrides, boranes ──

    [GeneratedRegex(
        @"(?i)\b(?:" +
            // Organolithiums
            @"n-?BuLi|sec-?BuLi|t-?BuLi|tert-?BuLi|MeLi|PhLi|" +
            @"(?:butyl|methyl|phenyl|vinyl|ethyl|isopropyl|hexyl)\s*lithium|" +
            @"lithium\s+(?:di(?:isopropyl)?amide|hexamethyldisilazide|tetramethylpiperidide)|" +
            @"LDA|LiHMDS|LiTMP|" +
            // Grignard / organomagnesium
            @"(?:methyl|ethyl|phenyl|vinyl|allyl|isopropyl|propyl|butyl|hexyl|cyclohexyl)\s*(?:magnesium|MgBr|MgCl|MgI)|" +
            @"(?:Me|Et|Ph|iPr|nBu|tBu|Cy|Vinyl|Allyl)MgBr|" +
            @"(?:Me|Et|Ph|iPr|nBu|tBu|Cy|Vinyl|Allyl)MgCl|" +
            @"Grignard|" +
            // Metal hydrides (strong)
            @"LiAlH[₄4]|lithium\s+alum(?:inium|inum)\s+hydride|" +
            @"NaH(?![CO])|sodium\s+hydride|" +
            @"KH\b|potassium\s+hydride|" +
            @"CaH[₂2]|calcium\s+hydride|" +
            @"DIBAL(?:-?H)?|diisobutylalum(?:inium|inum)\s+hydride|" +
            @"Red-?Al|sodium\s+bis\(2-methoxyethoxy\)alum(?:inium|inum)\s+hydride|" +
            @"L-?Selectride|K-?Selectride|" +
            @"Super-?Hydride|lithium\s+triethylborohydride|" +
            // Boranes
            @"BH[₃3](?![\w])|borane|9-?BBN|thexyl\s*borane|disiamylborane|" +
            @"borane[·.](?:THF|DMS|Me₂S|dimethyl\s+sulfide)|" +
            // Organozinc
            @"(?:Me|Et|Ph)[₂2]Zn|diethylzinc|dimethylzinc|" +
            // Other highly reactive
            @"butyllithium|KHMDS|NaHMDS|" +
            @"sodium\s+(?:bis\(trimethylsilyl\)amide|hexamethyldisilazide)" +
        @")\b",
        RegexOptions.Compiled)]
    private static partial Regex HighReactivityPattern();

    // ── Moderate-reactivity: mild reducing agents ──

    [GeneratedRegex(
        @"(?i)\b(?:" +
            @"NaBH[₄4]|sodium\s+borohydride|" +
            @"NaCNBH[₃3]|sodium\s+cyanoborohydride|" +
            @"NaBH\(OAc\)[₃3]|sodium\s+triacetoxyborohydride|" +
            @"Zn(?:BH[₄4])|zinc\s+borohydride|" +
            @"SnCl[₂2]|tin(?:\(II\))?\s+chloride|" +
            @"SmI[₂2]|samarium(?:\(II\))?\s+iodide" +
        @")\b",
        RegexOptions.Compiled)]
    private static partial Regex ModerateReactivityPattern();

    // ── Benign: mild bases, non-nucleophilic bases, common auxiliaries ──

    [GeneratedRegex(
        @"(?i)\b(?:" +
            @"(?:tri)?ethylamine|Et[₃3]N|TEA|NEt[₃3]|" +
            @"di(?:isopropyl)?ethylamine|DIPEA|DIEA|Hünig(?:'s)?\s+base|" +
            @"N,N-diisopropylethylamine|" +
            @"pyridine|DMAP|4-dimethylaminopyridine|" +
            @"imidazole|N-methylimidazole|NMI|" +
            @"DBU|DBN|DABCO|" +
            @"1,8-diazabicyclo\[5\.4\.0\]undec-7-ene|" +
            @"K[₂2]CO[₃3]|potassium\s+carbonate|" +
            @"Na[₂2]CO[₃3]|sodium\s+carbonate|" +
            @"NaHCO[₃3]|sodium\s+(?:bi)?carbonate|" +
            @"Cs[₂2]CO[₃3]|ces(?:ium|ium)\s+carbonate|" +
            @"Li[₂2]CO[₃3]|lithium\s+carbonate|" +
            @"CaCO[₃3]|calcium\s+carbonate|" +
            @"K[₃3]PO[₄4]|potassium\s+phosphate|" +
            @"KOH|NaOH|LiOH|" +
            @"potassium\s+hydroxide|sodium\s+hydroxide|lithium\s+hydroxide|" +
            @"KOt-?Bu|KOtBu|potassium\s+tert-?butoxide|" +
            @"NaOt-?Bu|NaOtBu|sodium\s+tert-?butoxide|" +
            @"NaOMe|NaOEt|sodium\s+methoxide|sodium\s+ethoxide|" +
            @"KOMe|KOEt|potassium\s+methoxide|potassium\s+ethoxide|" +
            @"proton\s+sponge" +
        @")\b",
        RegexOptions.Compiled)]
    private static partial Regex BenignPattern();

    /// <summary>
    /// Classifies a reagent name into a <see cref="ReactivityTier"/>.
    /// Returns <see cref="ReactivityTier.Unknown"/> when no pattern matches.
    /// </summary>
    public static ReactivityTier Classify(string? reagentName)
    {
        if (string.IsNullOrWhiteSpace(reagentName))
            return ReactivityTier.Unknown;

        if (HighReactivityPattern().IsMatch(reagentName))
            return ReactivityTier.High;

        if (ModerateReactivityPattern().IsMatch(reagentName))
            return ReactivityTier.Moderate;

        if (BenignPattern().IsMatch(reagentName))
            return ReactivityTier.Benign;

        return ReactivityTier.Unknown;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the reagent is reactive enough to
    /// require an explicit quench or controlled workup.
    /// </summary>
    public static bool RequiresQuench(string? reagentName) =>
        Classify(reagentName) == ReactivityTier.High;

    /// <summary>
    /// Returns <see langword="true"/> when the reagent may need workup
    /// attention depending on procedure context.
    /// </summary>
    public static bool MayRequireQuench(string? reagentName) =>
        Classify(reagentName) is ReactivityTier.High or ReactivityTier.Moderate;
}
