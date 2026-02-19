using ChemVerify.Core.Extractors;

namespace ChemVerify.Tests;

public class DoiClaimExtractorTests
{
    private readonly DoiClaimExtractor _extractor = new();

    [Fact]
    public void SingleDoi_ExtractedCorrectly()
    {
        string text = "As described (10.1021/ja00536a027).";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.Single(claims);
        Assert.Contains("10.1021/ja00536a027", claims[0].NormalizedValue);
    }

    [Fact]
    public void DuplicateDoi_MarkdownAndUrl_DeduplicatedToOne()
    {
        string text = "[Smith et al.](https://doi.org/10.1021/ja00536a027) " +
                       "See also https://doi.org/10.1021/ja00536a027 for details.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.Single(claims);
    }

    [Fact]
    public void DuplicateDoi_CaseInsensitive()
    {
        string text = "10.1021/JA00536A027 and also 10.1021/ja00536a027.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.Single(claims);
    }

    [Fact]
    public void TwoDifferentDois_BothExtracted()
    {
        string text = "See 10.1021/ja00536a027 and 10.1038/s41586-020-2649-2.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.Equal(2, claims.Count);
    }

    [Fact]
    public void GoogleSearchUrl_DoiExtractedCleanly()
    {
        // The regex should capture the DOI from inside a Google search URL
        string text = "https://www.google.com/search?q=https://doi.org/10.1021/ja00536a027";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        // Should produce claim(s) — at least one with the actual DOI
        Assert.NotEmpty(claims);
        Assert.Contains(claims, c => c.NormalizedValue.Contains("10.1021/ja00536a027"));
    }

    [Fact]
    public void TrailingPunctuation_Stripped()
    {
        string text = "Previous work (10.1021/ja00536a027).";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.Single(claims);
        Assert.DoesNotContain(")", claims[0].NormalizedValue);
        Assert.False(claims[0].NormalizedValue.EndsWith('.'), "Trailing period should be stripped");
    }
}
