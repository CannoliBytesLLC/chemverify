using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class MalformedChemicalTokenValidatorTests
{
    private readonly MalformedChemicalTokenValidator _validator = new();
    private readonly Guid _runId = Guid.NewGuid();

    private AiRun MakeRun(string text) => new()
    {
        Id = _runId,
        Mode = RunMode.VerifyOnly,
        InputText = text,
        CurrentHash = "test"
    };

    [Fact]
    public void UppercaseChemicalName_EmptyParens_Detected()
    {
        AiRun run = MakeRun("The reaction used NaOH () as the base.");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Contains(findings, f => f.Message.Contains("empty parentheses"));
    }

    [Fact]
    public void LowercaseChemicalName_EmptyParens_Detected()
    {
        AiRun run = MakeRun("sodium borohydride () was added.");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Contains(findings, f => f.Message.Contains("empty parentheses"));
    }

    [Fact]
    public void StandaloneDegreeC_Detected()
    {
        AiRun run = MakeRun("The reaction was run at °C for 2 h.");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Contains(findings, f => f.Message.Contains("°C"));
    }

    [Fact]
    public void ValidTemperature_NotFlagged()
    {
        AiRun run = MakeRun("The reaction was run at 25 °C for 2 h.");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.DoesNotContain(findings, f => f.Message.Contains("Standalone °C"));
    }

    [Fact]
    public void DroppedToken_ConsecutiveSpaces_Detected()
    {
        AiRun run = MakeRun("While  is inherently capable of reducing both aldehydes and ketones.");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Contains(findings, f => f.Message.Contains("dropped"));
    }

    [Fact]
    public void EmptyBoldMarkers_Detected()
    {
        AiRun run = MakeRun("using **** supported on wet silica gel");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Contains(findings, f => f.Message.Contains("Empty bold marker"));
    }

    [Fact]
    public void EmptyBoldMarkersWithSpace_Detected()
    {
        AiRun run = MakeRun("using ** ** supported on wet silica gel");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Contains(findings, f => f.Message.Contains("Empty bold marker"));
    }

    [Fact]
    public void CleanText_NoFindings()
    {
        AiRun run = MakeRun("NaBH4 (0.38 g, 10 mmol) was added portionwise at 0 °C.");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Empty(findings);
    }

    [Fact]
    public void EmptyText_NoFindings()
    {
        AiRun run = MakeRun("");
        IReadOnlyList<ValidationFinding> findings = _validator.Validate(_runId, [], run);

        Assert.Empty(findings);
    }
}
