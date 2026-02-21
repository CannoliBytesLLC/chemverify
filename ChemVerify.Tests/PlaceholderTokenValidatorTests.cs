using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class PlaceholderTokenValidatorTests
{
    private readonly PlaceholderTokenValidator _validator = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        Status = RunStatus.Completed,
        CurrentHash = "test"
    };

    [Fact]
    public void PrepositionFollowedByPunctuation_Detected()
    {
        var run = MakeRun("The reaction was carried out under .");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.PlaceholderOrMissingToken &&
            f.Message.Contains("under"));
    }

    [Fact]
    public void EmptyQuantityParens_Detected()
    {
        var run = MakeRun("The aqueous phase is extracted with ether ( mL).");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.PlaceholderOrMissingToken &&
            f.Message.Contains("mL"));
    }

    [Fact]
    public void AsterisksPlaceholder_Detected()
    {
        var run = MakeRun("The organics were dried over ****.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.PlaceholderOrMissingToken &&
            f.Message.Contains("asterisks"));
    }

    [Fact]
    public void NewBlankBond_Detected()
    {
        var run = MakeRun("The Grignard reacts to form a new  bond.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.PlaceholderOrMissingToken &&
            f.Message.Contains("bond"));
    }

    [Fact]
    public void CleanText_NoFindings()
    {
        var run = MakeRun("NaBH4 (0.38 g, 10 mmol) was added in THF (20 mL) under N2.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.PlaceholderOrMissingToken);
    }

    [Fact]
    public void StandalonePercent_InYield_Detected()
    {
        var run = MakeRun("The product was obtained in % yield.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.PlaceholderOrMissingToken &&
            f.Message.Contains("%"));
    }

    [Fact]
    public void SpaceBeforePercentInComposition_Detected()
    {
        var run = MakeRun("Purified by chromatography ( % EtOAc/hexanes).");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Kind == FindingKind.PlaceholderOrMissingToken);
    }
}
