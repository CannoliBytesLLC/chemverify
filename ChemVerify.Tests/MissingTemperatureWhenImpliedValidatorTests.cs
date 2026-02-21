using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class MissingTemperatureWhenImpliedValidatorTests
{
    private readonly MissingTemperatureWhenImpliedValidator _validator = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    [Fact]
    public void ImpliedTemp_NoTempClaim_Fails()
    {
        var run = MakeRun("NaBH4 was added dropwise over 10 min.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void ImpliedTemp_WithNumericTempClaim_Passes()
    {
        var run = MakeRun("NaBH4 was added dropwise at 0 °C over 10 min.");
        var tempClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0 °C",
            NormalizedValue = "0",
            Unit = "°C",
            JsonPayload = "{\"contextKey\":\"temp\",\"value\":0}"
        };
        var findings = _validator.Validate(run.Id, [tempClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void ImpliedTemp_WithRoomTemperature_Passes()
    {
        // RT is now a SymbolicTemperature claim emitted by the extractor
        var run = MakeRun("NaBH4 was added dropwise and stirred at room temperature.");
        var symbolicClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.SymbolicTemperature,
            RawText = "room temperature",
            NormalizedValue = "rt",
            JsonPayload = "{\"contextKey\":\"temp\",\"symbolic\":\"rt\"}"
        };
        var findings = _validator.Validate(run.Id, [symbolicClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Theory]
    [InlineData("maintained at rt for 12 h")]
    [InlineData("the reaction was performed at ambient temperature")]
    [InlineData("stirred at room temp overnight")]
    public void ImpliedTemp_WithRoomTempVariants_Passes(string text)
    {
        var run = MakeRun(text + " with dropwise addition.");
        var symbolicClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.SymbolicTemperature,
            RawText = "rt",
            NormalizedValue = "rt",
            JsonPayload = "{\"contextKey\":\"temp\",\"symbolic\":\"rt\"}"
        };
        var findings = _validator.Validate(run.Id, [symbolicClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void NoImpliedTemp_NoFindings()
    {
        var run = MakeRun("The compound was characterized by NMR.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Empty(findings);
    }

    [Fact]
    public void MaintainedUnderNitrogen_NotTreatedAsImpliedTemp()
    {
        var run = MakeRun("The reaction was maintained under nitrogen for 2 hours.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void HeatedTo_WithoutTemp_TreatedAsImpliedTemp()
    {
        var run = MakeRun("The mixture was heated to reflux for 30 minutes.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void HeatedTo_WithRefluxSymbolicClaim_NoFinding()
    {
        var run = MakeRun("The mixture was heated to reflux for 30 minutes.");
        var symbolicClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.SymbolicTemperature,
            RawText = "reflux",
            NormalizedValue = "reflux",
            JsonPayload = "{\"contextKey\":\"temp\",\"symbolic\":\"reflux\"}"
        };
        var findings = _validator.Validate(run.Id, [symbolicClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void Dropwise_WithIceBathSymbolicClaim_NoFinding()
    {
        var run = MakeRun("NaBH4 was added dropwise in an ice bath.");
        var symbolicClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.SymbolicTemperature,
            RawText = "ice bath",
            NormalizedValue = "ice_bath",
            JsonPayload = "{\"contextKey\":\"temp\",\"symbolic\":\"ice_bath\"}"
        };
        var findings = _validator.Validate(run.Id, [symbolicClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void CooledTo_WithTemp_NoFinding()
    {
        var run = MakeRun("The flask was cooled to 0 °C and stirred.");
        var tempClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0 °C",
            NormalizedValue = "0",
            Unit = "°C",
            JsonPayload = "{\"contextKey\":\"temp\"}"
        };
        var findings = _validator.Validate(run.Id, [tempClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }
}
