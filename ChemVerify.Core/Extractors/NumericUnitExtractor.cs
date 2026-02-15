using System.Text.RegularExpressions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Core.Extractors;

public class NumericUnitExtractor : IClaimExtractor
{
    // Matches patterns like "82%", "0.5 M", "2 h", "120 min", "78 °C", "-78C"
    private static readonly Regex NumericUnitRegex = new(
        @"(?<num>-?\d+(?:\.\d+)?)\s*(?<unit>%|°?C|M|h|min|mg|mL|g|L|K|mol|mmol|kPa|atm|ppm)",
        RegexOptions.Compiled);

    // Context labels commonly found near numeric values in chemistry text
    private static readonly Regex ContextLabelRegex = new(
        @"\b(yield|temp(?:erature)?|time|equiv|conc(?:entration)?|pressure|mass|volume|purity|conversion|selectivity|ee|dr)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Percent-specific context classifiers
    private static readonly Regex PercentYieldRegex = new(
        @"\byield\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentConcRegex = new(
        @"\b(HCl|NaOH|H2SO4|HNO3|KOH|NaHCO3|NH[34]|aq\b|aqueous|solution|w/w|v/v|wt\s*%|vol\s*%|conc\.?|dispersion)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentCompositionRegex = new(
        @"\b(silica|column|chromatography|eluent|hexanes?|EtOAc|ethyl\s+acetate|gradient|flash|TLC|Rf)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Time-action verb classifiers
    private static readonly Regex TimeAdditionRegex = new(
        @"\b(added?\s+(?:drop\s*wise\s+)?over|portion\s*wise(?:\s+over)?|drop\s*wise(?:\s+over)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimeStirRegex = new(
        @"\b(stirr?(?:ed|ing)?\s+for)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimeHoldRegex = new(
        @"\b(maintain(?:ed|ing)?\s+(?:at\s+.{1,20}?\s+)?for|held?\s+(?:at\s+.{1,20}?\s+)?for|kept\s+(?:at\s+.{1,20}?\s+)?for)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimeHeatRegex = new(
        @"\b(heat(?:ed|ing)?\s+(?:to\s+.{1,20}?\s+)?for|reflux(?:ed|ing)?\s+for)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Entity key heuristic: chemical name / reagent noun preceding a numeric value
    private static readonly Regex EntityTokenRegex = new(
        @"\b([A-Z][a-z]*(?:[A-Z][a-z]*)*(?:\d*)|"                                     // CamelCase (NaBH4, MeOH, etc.)
        + @"[A-Z][a-z]{2,}(?:\s+[a-z]{2,})?|"                                         // Capitalized noun phrase (Benzaldehyde, Ethyl acetate)
        + @"[a-z]{3,}(?:ene|ane|ine|ide|ate|ite|ol|one|ium|yne)|"                       // Chemical suffixes
        + @"(?:NaH|LiAlH4|LAH|NaBH4|BuLi|DIBAL|TBAF|KOH|NaOH|K2CO3|Cs2CO3|Pd|Ni|Cu|Zn|Mg|Fe|Rh|Ir|Ru)" // Common reagents
        + @")\b",
        RegexOptions.Compiled);

    private const int ContextWindowChars = 40;
    private const int EntityWindowChars = 35;

    public IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text)
    {
        List<ExtractedClaim> claims = new();
        MatchCollection matches = NumericUnitRegex.Matches(text);

        // Pre-compute step boundaries once for the whole text
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        foreach (Match match in matches)
        {
            string numericPart = match.Groups["num"].Value;
            string unitPart = match.Groups["unit"].Value;

            // Normalize bare C ? °C for temperature
            if (unitPart == "C")
            {
                unitPart = "°C";
            }

            string contextKey = ResolveContextKey(text, match.Index, unitPart);
            string? timeAction = null;

            if (contextKey == "time")
            {
                timeAction = ResolveTimeAction(text, match.Index);
            }

            string? jsonPayload = BuildJsonPayload(contextKey, timeAction);
            string? entityKey = ResolveEntityKey(text, match.Index, unitPart);
            int? stepIndex = StepSegmenter.GetStepIndex(steps, match.Index);

            claims.Add(new ExtractedClaim
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimType = ClaimType.NumericWithUnit,
                RawText = match.Value,
                NormalizedValue = numericPart,
                Unit = unitPart,
                SourceLocator = $"AnalyzedText:{match.Index}-{match.Index + match.Length}",
                JsonPayload = jsonPayload,
                EntityKey = entityKey,
                StepIndex = stepIndex
            });
        }

        return claims;
    }

    private static string ResolveContextKey(string text, int matchIndex, string unit)
    {
        // Definitive unit-based context — these units are unambiguous
        string? unitImpliedContext = unit switch
        {
            "°C" or "C" or "K" => "temp",
            "h" or "min" => "time",
            _ => null
        };

        if (unitImpliedContext is not null)
        {
            return unitImpliedContext;
        }

        // For percent, use specialized classifier
        if (unit == "%")
        {
            return ClassifyPercentContext(text, matchIndex);
        }

        // For ambiguous units, look at nearby text
        int windowStart = Math.Max(0, matchIndex - ContextWindowChars);
        int windowEnd = Math.Min(text.Length, matchIndex + ContextWindowChars);
        string window = text[windowStart..windowEnd];

        Match labelMatch = ContextLabelRegex.Match(window);
        if (labelMatch.Success)
        {
            return labelMatch.Value.ToLowerInvariant();
        }

        // Last-resort fallback from unit
        return unit switch
        {
            "M" => "conc",
            _ => string.Empty
        };
    }

    private static string ClassifyPercentContext(string text, int matchIndex)
    {
        int windowStart = Math.Max(0, matchIndex - ContextWindowChars);
        int windowEnd = Math.Min(text.Length, matchIndex + ContextWindowChars);
        string window = text[windowStart..windowEnd];

        // Order matters: check yield first (most specific intent)
        if (PercentYieldRegex.IsMatch(window))
        {
            return "yield";
        }

        // Chromatography / eluent composition — not comparable
        if (PercentCompositionRegex.IsMatch(window))
        {
            return "composition";
        }

        // Solution concentration
        if (PercentConcRegex.IsMatch(window))
        {
            return "conc";
        }

        // No recognizable context — leave empty so it becomes NotComparable
        return string.Empty;
    }

    private static string? ResolveTimeAction(string text, int matchIndex)
    {
        int windowStart = Math.Max(0, matchIndex - ContextWindowChars);
        int windowEnd = Math.Min(text.Length, matchIndex + ContextWindowChars);
        string window = text[windowStart..windowEnd];

        if (TimeAdditionRegex.IsMatch(window)) return "addition";
        if (TimeStirRegex.IsMatch(window)) return "stir";
        if (TimeHoldRegex.IsMatch(window)) return "hold";
        if (TimeHeatRegex.IsMatch(window)) return "heat";

        return null;
    }

    private static string? BuildJsonPayload(string contextKey, string? timeAction)
    {
        if (contextKey.Length == 0 && timeAction is null)
        {
            return null;
        }

        if (timeAction is not null)
        {
            return $"{{\"contextKey\":\"{contextKey}\",\"timeAction\":\"{timeAction}\"}}";
        }

        return $"{{\"contextKey\":\"{contextKey}\"}}";
    }

    /// <summary>
    /// Looks up to <see cref="EntityWindowChars"/> characters to the left of a numeric match
    /// for the nearest chemical token / reagent noun to use as an entity label.
    /// Returns null for temperature, time, yield, and composition claims
    /// (where entity scoping is not meaningful).
    /// </summary>
    private static string? ResolveEntityKey(string text, int matchIndex, string unit)
    {
        // Temperature, time, yield, and composition are already scoped by contextKey
        if (unit is "°C" or "C" or "K" or "h" or "min" or "%")
        {
            return null;
        }

        int windowStart = Math.Max(0, matchIndex - EntityWindowChars);
        string window = text[windowStart..matchIndex];

        // Search for the rightmost (nearest) chemical token in the window
        MatchCollection entityMatches = EntityTokenRegex.Matches(window);
        if (entityMatches.Count == 0)
        {
            return null;
        }

        string token = entityMatches[^1].Value.Trim();

        // Ignore very short or generic tokens that are not useful as entity keys
        if (token.Length < 2 || IsGenericWord(token))
        {
            return null;
        }

        return token.ToLowerInvariant();
    }

    private static bool IsGenericWord(string token)
    {
        return token.Equals("The", StringComparison.OrdinalIgnoreCase)
            || token.Equals("was", StringComparison.OrdinalIgnoreCase)
            || token.Equals("with", StringComparison.OrdinalIgnoreCase)
            || token.Equals("and", StringComparison.OrdinalIgnoreCase)
            || token.Equals("for", StringComparison.OrdinalIgnoreCase)
            || token.Equals("the", StringComparison.OrdinalIgnoreCase)
            || token.Equals("into", StringComparison.OrdinalIgnoreCase)
            || token.Equals("from", StringComparison.OrdinalIgnoreCase);
    }
}

