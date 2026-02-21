using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class ConcentrationSanityValidatorTests
{
    private readonly ConcentrationSanityValidator _validator = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    [Fact]
    public void HClInDioxane_RecognizedAsKnownForm()
    {
        var run = MakeRun(
            "treated with a solution of HCl in dioxane (4 N, 0.5 mL).");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f =>
            f.Status == ValidationStatus.Pass
            && f.Message.Contains("recognized commercial reagent form"));
    }

    [Fact]
    public void BuLiInHexanes_RecognizedAsKnownForm()
    {
        var run = MakeRun(
            "n-BuLi in hexanes (2.5 M, 4.0 mL) was added dropwise.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void NBuLi_Alias_RecognizedAsKnownForm()
    {
        var run = MakeRun(
            "nBuLi in hexanes (2.5 M, 4.0 mL) was added dropwise.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void Butyllithium_FullName_RecognizedAsKnownForm()
    {
        var run = MakeRun(
            "n-butyllithium in hexanes (2.5 M, 4.0 mL) was added dropwise.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void GrignardInTHF_RecognizedAsKnownForm()
    {
        var run = MakeRun(
            "PhMgBr in THF (1.0 M, 5.0 mL) was added slowly.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void NoReagentInSolventPattern_NoFindings()
    {
        var run = MakeRun("NaBH4 (0.38 g, 10 mmol) was added portionwise.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Empty(findings);
    }

    [Fact]
    public void EthylmagnesiumChloride_FullName_RecognizedAsKnownForm()
    {
        var run = MakeRun(
            "Ethylmagnesium chloride in THF (2.0 M, 1.0 mL) was added at 0 °C.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void PhenylmagnesiumChloride_FullName_RecognizedAsKnownForm()
    {
        var run = MakeRun(
            "Phenylmagnesium chloride in THF (2.0 M, 0.60 mL) was added under N2.");
        var findings = _validator.Validate(run.Id, [], run);

        Assert.Contains(findings, f => f.Status == ValidationStatus.Pass);
    }
}
