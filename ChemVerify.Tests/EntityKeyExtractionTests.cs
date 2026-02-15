using ChemVerify.Abstractions.Enums;
using ChemVerify.Core.Extractors;

namespace ChemVerify.Tests;

public class EntityKeyExtractionTests
{
    private readonly NumericUnitExtractor _extractor = new();

    [Fact]
    public void Benzaldehyde_MassGets_EntityKey()
    {
        string text = "Benzaldehyde (1.06 g, 10 mmol) was dissolved in 10 mL of MeOH.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var massClaim = claims.FirstOrDefault(c => c.RawText == "1.06 g");
        Assert.NotNull(massClaim);
        Assert.NotNull(massClaim.EntityKey);
        Assert.Contains("benzaldehyde", massClaim.EntityKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NaBH4_MassGets_DifferentEntityKey()
    {
        string text = "Benzaldehyde (1.06 g, 10 mmol) was dissolved in MeOH. NaBH4 (0.38 g, 10 mmol) was added.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var benzClaim = claims.FirstOrDefault(c => c.RawText == "1.06 g");
        var nabh4Claim = claims.FirstOrDefault(c => c.RawText == "0.38 g");

        Assert.NotNull(benzClaim);
        Assert.NotNull(nabh4Claim);
        Assert.NotNull(benzClaim.EntityKey);
        Assert.NotNull(nabh4Claim.EntityKey);
        Assert.NotEqual(benzClaim.EntityKey, nabh4Claim.EntityKey);
    }

    [Fact]
    public void Temperature_NoEntityKey()
    {
        string text = "The reaction was heated to 80 °C.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var tempClaim = claims.FirstOrDefault(c => c.Unit == "°C");
        Assert.NotNull(tempClaim);
        Assert.Null(tempClaim.EntityKey);
    }

    [Fact]
    public void Time_NoEntityKey()
    {
        string text = "The mixture was stirred for 2 h.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var timeClaim = claims.FirstOrDefault(c => c.Unit == "h");
        Assert.NotNull(timeClaim);
        Assert.Null(timeClaim.EntityKey);
    }

    [Fact]
    public void StepIndex_AssignedToAllClaims()
    {
        string text = "Benzaldehyde (1.06 g) was added. NaBH4 (0.38 g) was used.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.All(claims, c => Assert.NotNull(c.StepIndex));
    }

    [Fact]
    public void DifferentSentences_DifferentStepIndices()
    {
        string text = "Benzaldehyde (1.06 g) was added. NaBH4 (0.38 g) was used.";
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var benzClaim = claims.FirstOrDefault(c => c.RawText == "1.06 g");
        var nabh4Claim = claims.FirstOrDefault(c => c.RawText == "0.38 g");

        Assert.NotNull(benzClaim);
        Assert.NotNull(nabh4Claim);
        // Claims in different sentences should have different step indices
        Assert.NotEqual(benzClaim.StepIndex, nabh4Claim.StepIndex);
    }
}
