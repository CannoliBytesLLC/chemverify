using ChemVerify.Abstractions.Enums;
using ChemVerify.Core.Extractors;

namespace ChemVerify.Tests;

public class ReagentRoleExtractorTests
{
    private readonly ReagentRoleExtractor _extractor = new();

    [Fact]
    public void Solvent_EthanolDetected_WithRoleAndEntityKey()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "The compound was dissolved in ethanol.");

        var solvent = claims.FirstOrDefault(c => c.ClaimType == ClaimType.SolventMention);
        Assert.NotNull(solvent);
        Assert.Contains("ethanol", solvent.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(solvent.EntityKey);
        Assert.NotNull(solvent.JsonPayload);
        Assert.Contains("\"role\":\"solvent\"", solvent.JsonPayload);
    }

    [Fact]
    public void Base_K2CO3Detected()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "K2CO3 (1.38 g, 10 mmol) was added.");

        var baseClaim = claims.FirstOrDefault(c =>
            c.ClaimType == ClaimType.ReagentMention &&
            c.JsonPayload is not null &&
            c.JsonPayload.Contains("\"role\":\"base\""));

        Assert.NotNull(baseClaim);
        Assert.Contains("K2CO3", baseClaim.RawText);
    }

    [Fact]
    public void Reductant_NaBH4Detected()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "NaBH4 (0.38 g, 10 mmol) was added portionwise.");

        var reductant = claims.FirstOrDefault(c =>
            c.ClaimType == ClaimType.ReagentMention &&
            c.JsonPayload is not null &&
            c.JsonPayload.Contains("\"role\":\"reductant\""));

        Assert.NotNull(reductant);
        Assert.Equal("NaBH4", reductant.RawText);
    }

    [Fact]
    public void Acid_HClDetected()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "The mixture was acidified with HCl.");

        var acid = claims.FirstOrDefault(c =>
            c.ClaimType == ClaimType.ReagentMention &&
            c.JsonPayload is not null &&
            c.JsonPayload.Contains("\"role\":\"acid\""));

        Assert.NotNull(acid);
        Assert.Equal("HCl", acid.RawText);
    }

    [Fact]
    public void Atmosphere_N2Detected()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "The reaction was carried out under N2.");

        var atm = claims.FirstOrDefault(c => c.ClaimType == ClaimType.AtmosphereCondition);
        Assert.NotNull(atm);
        Assert.Equal("nitrogen", atm.NormalizedValue);
        Assert.NotNull(atm.EntityKey);
    }

    [Fact]
    public void Dryness_AnhydrousDetected()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "Anhydrous THF was used as the solvent.");

        var dry = claims.FirstOrDefault(c => c.ClaimType == ClaimType.DrynessCondition);
        Assert.NotNull(dry);
        Assert.Contains("anhydrous", dry.RawText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StepIndex_AssignedToAllClaims()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "NaBH4 was added. The mixture was stirred in THF.");

        Assert.All(claims, c => Assert.NotNull(c.StepIndex));
    }

    [Fact]
    public void EntityKey_PresentForReagentClaims()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "LiAlH4 (0.5 g) was used as reductant.");

        var reductant = claims.FirstOrDefault(c =>
            c.ClaimType == ClaimType.ReagentMention &&
            c.JsonPayload is not null &&
            c.JsonPayload.Contains("\"role\":\"reductant\""));

        Assert.NotNull(reductant);
        Assert.NotNull(reductant.EntityKey);
        Assert.Contains("lialh4", reductant.EntityKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DifferentSentences_DifferentStepIndices()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "NaBH4 was added to the flask. Ethanol was used as solvent.");

        var nabh4 = claims.FirstOrDefault(c =>
            c.ClaimType == ClaimType.ReagentMention &&
            c.RawText == "NaBH4");
        var ethanol = claims.FirstOrDefault(c =>
            c.ClaimType == ClaimType.SolventMention &&
            c.RawText.Equals("Ethanol", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(nabh4);
        Assert.NotNull(ethanol);
        Assert.NotEqual(nabh4.StepIndex, ethanol.StepIndex);
    }

    [Theory]
    [InlineData("nitrogen-containing scaffold")]
    [InlineData("nitrogen functionality adjustment")]
    [InlineData("a nitrogen-based heterocycle")]
    [InlineData("nitrogen-rich compound")]
    [InlineData("argon-based plasma treatment")]
    public void Atmosphere_BareNitrogenInStructuralContext_NotExtracted(string text)
    {
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.DoesNotContain(claims, c => c.ClaimType == ClaimType.AtmosphereCondition);
    }

    [Theory]
    [InlineData("The reaction was carried out under nitrogen.")]
    [InlineData("The flask was purged with argon before use.")]
    [InlineData("Degassed with nitrogen for 15 min.")]
    [InlineData("Performed in an atmosphere of argon.")]
    [InlineData("blanketed with N2 throughout.")]
    [InlineData("maintained under an Ar atmosphere.")]
    [InlineData("The reaction was run under N₂.")]
    [InlineData("stirred under a hydrogen balloon at room temperature.")]
    [InlineData("The solvent was sparged with nitrogen for 10 min.")]
    public void Atmosphere_ContextualGasReference_Extracted(string text)
    {
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        Assert.Contains(claims, c => c.ClaimType == ClaimType.AtmosphereCondition);
    }

    [Fact]
    public void Atmosphere_HydrogenBalloon_NormalizedToHydrogen()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "The flask was evacuated and backfilled with hydrogen, then stirred under a hydrogen balloon.");

        var atm = claims.FirstOrDefault(c => c.ClaimType == ClaimType.AtmosphereCondition);
        Assert.NotNull(atm);
        Assert.Equal("hydrogen", atm.NormalizedValue);
    }

    [Theory]
    [InlineData("stirred at room temperature", "rt")]
    [InlineData("maintained at rt for 12 h", "rt")]
    [InlineData("performed at ambient temperature", "rt")]
    [InlineData("the mixture was refluxed for 2 h", "reflux")]
    [InlineData("kept at reflux overnight", "reflux")]
    [InlineData("cooled in an ice bath", "ice_bath")]
    [InlineData("ice-water bath was used", "ice_bath")]
    public void SymbolicTemperature_Extracted(string text, string expectedNormalized)
    {
        var claims = _extractor.Extract(Guid.NewGuid(), text);

        var tempClaim = claims.FirstOrDefault(c => c.ClaimType == ClaimType.SymbolicTemperature);
        Assert.NotNull(tempClaim);
        Assert.Equal(expectedNormalized, tempClaim.NormalizedValue);
        Assert.Contains("\"contextKey\":\"temp\"", tempClaim.JsonPayload);
    }

    [Fact]
    public void SymbolicTemperature_RoomTemp_HasStepIndex()
    {
        var claims = _extractor.Extract(Guid.NewGuid(),
            "NaBH4 was added. The mixture was stirred at room temperature.");

        var tempClaim = claims.FirstOrDefault(c => c.ClaimType == ClaimType.SymbolicTemperature);
        Assert.NotNull(tempClaim);
        Assert.NotNull(tempClaim.StepIndex);
    }
}
