using System.Text.RegularExpressions;
using Aegis.Core.Enums;
using Aegis.Core.Interfaces;
using Aegis.Core.Models;

namespace Aegis.Infrastructure.Extractors;

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

    private const int ContextWindowChars = 40;

    public IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text)
    {
        List<ExtractedClaim> claims = new();
        MatchCollection matches = NumericUnitRegex.Matches(text);

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

            claims.Add(new ExtractedClaim
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimType = ClaimType.NumericWithUnit,
                RawText = match.Value,
                NormalizedValue = numericPart,
                Unit = unitPart,
                SourceLocator = $"Output:{match.Index}-{match.Index + match.Length}",
                JsonPayload = contextKey.Length > 0
                    ? $"{{\"contextKey\":\"{contextKey}\"}}"
                    : null
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
            "%" => "yield",
            "M" => "conc",
            _ => string.Empty
        };
    }
}
