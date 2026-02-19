using System.Text.RegularExpressions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Extractors;

public class DoiClaimExtractor : IClaimExtractor
{
    // Permissive extraction: capture DOI-like strings including characters that
    // may be invalid. The DoiFormatValidator enforces strict format downstream.
    private static readonly Regex DoiRegex = new(
        @"10\.\d{4,9}/[^\s]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Strip common trailing punctuation that is almost never part of a real DOI
    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':'];

    // Delimiters that signal the end of a DOI within markdown/URL contexts
    private static readonly char[] DoiDelimiters = [']', ')', '"', '\'', '<', '>'];

    public IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text)
    {
        List<ExtractedClaim> claims = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        MatchCollection matches = DoiRegex.Matches(text);

        foreach (Match match in matches)
        {
            string raw = match.Value;

            // Truncate at the first markdown/URL delimiter
            int delimIndex = raw.IndexOfAny(DoiDelimiters);
            if (delimIndex > 0)
            {
                raw = raw[..delimIndex];
            }

            raw = raw.TrimEnd(TrailingPunctuation);

            // Skip if this DOI was already captured (handles markdown link + URL duplicates)
            string normalized = raw.ToLowerInvariant();
            if (!seen.Add(normalized))
                continue;

            claims.Add(new ExtractedClaim
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimType = ClaimType.CitationDoi,
                RawText = raw,
                NormalizedValue = normalized,
                SourceLocator = $"AnalyzedText:{match.Index}-{match.Index + raw.Length}"
            });
        }

        return claims;
    }
}

