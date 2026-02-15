using System.Text.RegularExpressions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Core.Extractors;

/// <summary>
/// Detects chemical tokens and classifies obvious roles (solvent, base, acid, reductant)
/// plus atmosphere/dryness conditions. Each claim carries StepIndex and EntityKey.
/// </summary>
public class ReagentRoleExtractor : IClaimExtractor
{
    // ── Reagent role patterns ────────────────────────────────────────────
    private static readonly (Regex Pattern, string Role)[] ReagentPatterns =
    [
        // Reductants
        (new Regex(@"\b(NaBH4|sodium\s+borohydride|LiAlH4|lithium\s+alumin\w+\s+hydride|LAH|DIBAL(?:-H)?|L-Selectride|K-Selectride|Red-Al)\b",
            RegexOptions.Compiled), "reductant"),

        // Bases
        (new Regex(@"\b(NaH|sodium\s+hydride|NaOMe|sodium\s+methoxide|NaOEt|sodium\s+ethoxide|KOtBu|potassium\s+tert-butoxide|K2CO3|Cs2CO3|Na2CO3|NaHCO3|Et3N|triethylamine|TEA|DIPEA|[Hh]ünig'?s?\s+base|DBU|DMAP|pyridine|imidazole|LDA|LiHMDS|NaHMDS|KHMDS|n-BuLi|t-BuLi|s-BuLi|BuLi)\b",
            RegexOptions.Compiled), "base"),

        // Acids
        (new Regex(@"\b(HCl|hydrochloric\s+acid|H2SO4|sulfuric\s+acid|HNO3|nitric\s+acid|AcOH|acetic\s+acid|TFA|trifluoroacetic\s+acid|p-?TsOH|PTSA|CSA|camphorsulfonic\s+acid|HBF4|H3PO4|TfOH|triflic\s+acid)\b",
            RegexOptions.Compiled), "acid"),

        // Oxidants
        (new Regex(@"\b(mCPBA|PDC|PCC|DMP|Dess-Martin|IBX|TEMPO|NaOCl|KMnO4|OsO4|Swern|Jones)\b",
            RegexOptions.Compiled), "oxidant"),

        // Coupling / catalyst
        (new Regex(@"\b(Pd\(PPh[₃3]\)[₄4]|Pd2\(dba\)3|Pd\(OAc\)2|PdCl2|Ni\(cod\)2|CuI|CuBr|ZnCl2)\b",
            RegexOptions.Compiled), "catalyst"),

        // Grignard / organometallic
        (new Regex(@"\b([Gg]rignard|MgBr|MgCl|organolithium|organomagnesium|organozinc)\b",
            RegexOptions.Compiled), "organometallic"),
    ];

    // ── Solvent patterns ─────────────────────────────────────────────────
    private static readonly Regex SolventRegex = new(
        @"\b(THF|tetrahydrofuran|DCM|CH2Cl2|dichloromethane|DMF|dimethylformamide|DMSO|dimethyl\s+sulfoxide|"
        + @"MeCN|acetonitrile|toluene|benzene|hexane|hexanes|pentane|heptane|"
        + @"diethyl\s+ether|Et2O|ether|MTBE|dioxane|1,4-dioxane|"
        + @"EtOAc|ethyl\s+acetate|MeOH|methanol|EtOH|ethanol|iPrOH|isopropanol|"
        + @"acetone|chloroform|CHCl3|DME|dimethoxyethane|NMP|"
        + @"water|H2O|brine)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Atmosphere patterns ──────────────────────────────────────────────
    private static readonly Regex AtmosphereRegex = new(
        @"\b((?:under\s+)?(?:N2|nitrogen|argon|Ar|inert\s+atmosphere|inert\s+gas)|"
        + @"(?:in|under)\s+(?:an?\s+)?(?:atmosphere\s+of\s+)?(?:nitrogen|argon|N2|Ar)|"
        + @"(?:open\s+to\s+)?air)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Dryness patterns ─────────────────────────────────────────────────
    private static readonly Regex DrynessRegex = new(
        @"\b(anhydrous|dry|dried|oven-dried|flame-dried|molecular\s+sieves|"
        + @"freshly\s+distilled|Schlenk|glovebox)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text)
    {
        List<ExtractedClaim> claims = new();
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        // Reagent roles
        foreach ((Regex pattern, string role) in ReagentPatterns)
        {
            foreach (Match m in pattern.Matches(text))
            {
                string token = m.Value.Trim();
                claims.Add(new ExtractedClaim
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ClaimType = ClaimType.ReagentMention,
                    RawText = token,
                    NormalizedValue = token,
                    SourceLocator = $"AnalyzedText:{m.Index}-{m.Index + m.Length}",
                    JsonPayload = $"{{\"role\":\"{role}\",\"token\":\"{EscapeJson(token)}\"}}",
                    EntityKey = token.ToLowerInvariant(),
                    StepIndex = StepSegmenter.GetStepIndex(steps, m.Index)
                });
            }
        }

        // Solvents
        foreach (Match m in SolventRegex.Matches(text))
        {
            string token = m.Value.Trim();
            claims.Add(new ExtractedClaim
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimType = ClaimType.SolventMention,
                RawText = token,
                NormalizedValue = token,
                SourceLocator = $"AnalyzedText:{m.Index}-{m.Index + m.Length}",
                JsonPayload = $"{{\"role\":\"solvent\",\"token\":\"{EscapeJson(token)}\"}}",
                EntityKey = token.ToLowerInvariant(),
                StepIndex = StepSegmenter.GetStepIndex(steps, m.Index)
            });
        }

        // Atmosphere
        foreach (Match m in AtmosphereRegex.Matches(text))
        {
            string token = m.Value.Trim();
            claims.Add(new ExtractedClaim
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimType = ClaimType.AtmosphereCondition,
                RawText = token,
                NormalizedValue = NormalizeAtmosphere(token),
                SourceLocator = $"AnalyzedText:{m.Index}-{m.Index + m.Length}",
                JsonPayload = $"{{\"role\":\"atmosphere\",\"token\":\"{EscapeJson(token)}\"}}",
                EntityKey = NormalizeAtmosphere(token),
                StepIndex = StepSegmenter.GetStepIndex(steps, m.Index)
            });
        }

        // Dryness
        foreach (Match m in DrynessRegex.Matches(text))
        {
            string token = m.Value.Trim();
            claims.Add(new ExtractedClaim
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimType = ClaimType.DrynessCondition,
                RawText = token,
                NormalizedValue = token.ToLowerInvariant(),
                SourceLocator = $"AnalyzedText:{m.Index}-{m.Index + m.Length}",
                JsonPayload = $"{{\"role\":\"dryness\",\"token\":\"{EscapeJson(token)}\"}}",
                EntityKey = token.ToLowerInvariant(),
                StepIndex = StepSegmenter.GetStepIndex(steps, m.Index)
            });
        }

        return claims;
    }

    private static string NormalizeAtmosphere(string token)
    {
        string lower = token.ToLowerInvariant();
        if (lower.Contains("air")) return "air";
        if (lower.Contains("argon") || lower.Contains("ar")) return "argon";
        return "nitrogen";
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
