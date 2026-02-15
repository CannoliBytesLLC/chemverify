using ChemVerify.Core.Validation;

namespace ChemVerify.Tests;

public class EvidenceLocatorTests
{
    [Fact]
    public void TryParse_ValidLocator_ReturnsTrue()
    {
        bool ok = EvidenceLocator.TryParse("AnalyzedText:42-55", out int start, out int end);

        Assert.True(ok);
        Assert.Equal(42, start);
        Assert.Equal(55, end);
    }

    [Fact]
    public void TryParse_ZeroZero_ReturnsTrue()
    {
        bool ok = EvidenceLocator.TryParse("AnalyzedText:0-0", out int start, out int end);

        Assert.True(ok);
        Assert.Equal(0, start);
        Assert.Equal(0, end);
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        bool ok = EvidenceLocator.TryParse(null, out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_WrongPrefix_ReturnsFalse()
    {
        bool ok = EvidenceLocator.TryParse("SomeOtherRef:42-55", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_NoDash_ReturnsFalse()
    {
        bool ok = EvidenceLocator.TryParse("AnalyzedText:42", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_EndBeforeStart_ReturnsFalse()
    {
        bool ok = EvidenceLocator.TryParse("AnalyzedText:55-42", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_NegativeStart_ReturnsFalse()
    {
        bool ok = EvidenceLocator.TryParse("AnalyzedText:-1-5", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_NonNumeric_ReturnsFalse()
    {
        bool ok = EvidenceLocator.TryParse("AnalyzedText:abc-def", out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ExtractSnippet_ValidRange_ReturnsContextWindow()
    {
        string text = "The reaction was heated to 80 °C for 12 h, affording 92% yield.";
        // "80" is at index 27-29
        string? snippet = EvidenceLocator.ExtractSnippet(text, 27, 29, 10);

        Assert.NotNull(snippet);
        Assert.Contains("80", snippet);
        Assert.StartsWith("…", snippet); // clampedStart > 0
    }

    [Fact]
    public void ExtractSnippet_AtStart_NoPrefixEllipsis()
    {
        string text = "80 °C was used.";
        string? snippet = EvidenceLocator.ExtractSnippet(text, 0, 2, 10);

        Assert.NotNull(snippet);
        Assert.StartsWith("80", snippet);
        Assert.Contains("…", snippet); // suffix ellipsis since text continues
    }

    [Fact]
    public void ExtractSnippet_AtEnd_NoSuffixEllipsis()
    {
        string text = "Heated to 80";
        string? snippet = EvidenceLocator.ExtractSnippet(text, 10, 12, 10);

        Assert.NotNull(snippet);
        Assert.EndsWith("80", snippet);
    }

    [Fact]
    public void ExtractSnippet_NullText_ReturnsNull()
    {
        string? snippet = EvidenceLocator.ExtractSnippet(null, 0, 5);

        Assert.Null(snippet);
    }

    [Fact]
    public void ExtractSnippet_EmptyText_ReturnsNull()
    {
        string? snippet = EvidenceLocator.ExtractSnippet("", 0, 5);

        Assert.Null(snippet);
    }

    [Fact]
    public void ExtractSnippet_StartBeyondText_ReturnsNull()
    {
        string? snippet = EvidenceLocator.ExtractSnippet("short", 100, 105);

        Assert.Null(snippet);
    }

    [Fact]
    public void ExtractSnippet_NegativeStart_ReturnsNull()
    {
        string? snippet = EvidenceLocator.ExtractSnippet("text", -1, 2);

        Assert.Null(snippet);
    }
}
