using ChemVerify.Abstractions.Enums;
using ChemVerify.Core.Extractors;

namespace ChemVerify.Tests;

public class RangeClaimExtractionTests
{
    private readonly NumericUnitExtractor _extractor = new();

    [Fact]
    public void TildeRange_Yield_ParsedAsRange_NotNegative()
    {
        string text = "The product was obtained in ~80-85% yield.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        // Should produce a single range claim, not "-85%"
        var yieldClaim = claims.FirstOrDefault(c => c.Unit == "%");
        Assert.NotNull(yieldClaim);
        Assert.Equal("85", yieldClaim.NormalizedValue);
        Assert.DoesNotContain("-85", yieldClaim.NormalizedValue);

        // JsonPayload should contain rangeLow and rangeHigh
        Assert.NotNull(yieldClaim.JsonPayload);
        Assert.Contains("\"rangeLow\":\"80\"", yieldClaim.JsonPayload);
        Assert.Contains("\"rangeHigh\":\"85\"", yieldClaim.JsonPayload);
    }

    [Fact]
    public void EnDashRange_Temperature_ParsedAsRange()
    {
        string text = "The reaction was heated to 60–65 °C for 2 h.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var tempClaim = claims.FirstOrDefault(c =>
            c.Unit == "°C" && c.JsonPayload is not null && c.JsonPayload.Contains("rangeLow"));
        Assert.NotNull(tempClaim);
        Assert.Equal("65", tempClaim.NormalizedValue);
        Assert.Contains("\"rangeLow\":\"60\"", tempClaim.JsonPayload);
        Assert.Contains("\"rangeHigh\":\"65\"", tempClaim.JsonPayload);
    }

    [Fact]
    public void TrueNegativeTemperature_PreservedAsNegative()
    {
        // "-78 °C" is a real negative temperature, not a range
        string text = "The reaction was cooled to -78 °C.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var tempClaim = claims.FirstOrDefault(c => c.Unit == "°C");
        Assert.NotNull(tempClaim);
        Assert.Equal("-78", tempClaim.NormalizedValue);
    }

    [Fact]
    public void DecimalRange_ParsedCorrectly()
    {
        string text = "Purity was measured at 98.5-99.2% by HPLC.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var purityClaim = claims.FirstOrDefault(c =>
            c.Unit == "%" && c.JsonPayload is not null && c.JsonPayload.Contains("rangeLow"));
        Assert.NotNull(purityClaim);
        Assert.Equal("99.2", purityClaim.NormalizedValue);
        Assert.Contains("\"rangeLow\":\"98.5\"", purityClaim.JsonPayload);
        Assert.Contains("\"rangeHigh\":\"99.2\"", purityClaim.JsonPayload);
    }

    [Fact]
    public void ApproxRange_WithTilde_IncludesContextKey()
    {
        string text = "The yield was ~80-85%.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var yieldClaim = claims.FirstOrDefault(c => c.Unit == "%");
        Assert.NotNull(yieldClaim);
        Assert.NotNull(yieldClaim.JsonPayload);
        Assert.Contains("\"contextKey\":\"yield\"", yieldClaim.JsonPayload);
    }

    [Fact]
    public void SingleNegative_NoPrecedingDigit_NotTreatedAsRange()
    {
        // Standalone negative at start of text is a true negative
        string text = "-20 °C was maintained throughout.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var tempClaim = claims.FirstOrDefault(c => c.Unit == "°C");
        Assert.NotNull(tempClaim);
        Assert.Equal("-20", tempClaim.NormalizedValue);
    }

    [Fact]
    public void RangeClaim_StepIndex_Assigned()
    {
        string text = "The product was obtained in ~80-85% yield.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var yieldClaim = claims.FirstOrDefault(c => c.Unit == "%");
        Assert.NotNull(yieldClaim);
        Assert.NotNull(yieldClaim.StepIndex);
    }
}
