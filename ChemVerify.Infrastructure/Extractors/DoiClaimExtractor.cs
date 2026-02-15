using System.Text.RegularExpressions;
using ChemVerify.Core.Enums;
using ChemVerify.Core.Interfaces;
using ChemVerify.Core.Models;

namespace ChemVerify.Infrastructure.Extractors;

public class DoiClaimExtractor : IClaimExtractor
{
    private static readonly Regex DoiRegex = new(
        @"10\.\d{4,9}/[-._;()/:A-Z0-9]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text)
    {
        List<ExtractedClaim> claims = new();
        MatchCollection matches = DoiRegex.Matches(text);

        foreach (Match match in matches)
        {
            claims.Add(new ExtractedClaim
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimType = ClaimType.CitationDoi,
                RawText = match.Value,
                NormalizedValue = match.Value.ToLowerInvariant(),
                SourceLocator = $"Output:{match.Index}-{match.Index + match.Length}"
            });
        }

        return claims;
    }
}

