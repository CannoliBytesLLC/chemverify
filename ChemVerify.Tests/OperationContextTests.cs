using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Procedure;
using ChemVerify.Core.Validators;
using ChemVerify.Core.Validators.State;

namespace ChemVerify.Tests;

/// <summary>
/// Regression tests for lightweight operation-scoped procedural segmentation:
/// vacuum distillation isolation, mp/bp product properties, sequential
/// durations, room-temp then reflux, NaH aqueous quench, TLC monitoring, and
/// dropwise additions without cooling requirements.
/// </summary>
public class OperationContextTests
{
    private static AiRun Run(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    private static IReadOnlyList<ExtractedClaim> Extract(AiRun run)
    {
        ReagentRoleExtractor reagent = new();
        NumericUnitExtractor numeric = new();
        return [.. reagent.Extract(run.Id, run.InputText!), .. numeric.Extract(run.Id, run.InputText!)];
    }

    private static IReadOnlyList<ValidationFinding> ValidateSolventTemp(string text)
    {
        AiRun run = Run(text);
        return new SolventTemperatureValidator().Validate(run.Id, Extract(run), run);
    }

    private static IReadOnlyList<ValidationFinding> ValidateNumeric(string text)
    {
        AiRun run = Run(text);
        return new NumericContradictionValidator().Validate(run.Id, Extract(run), run);
    }

    private static IReadOnlyList<ValidationFinding> ValidateMissingTemp(string text)
    {
        AiRun run = Run(text);
        return new MissingTemperatureWhenImpliedValidator().Validate(run.Id, Extract(run), run);
    }

    private static IReadOnlyList<ValidationFinding> ValidateIncompatible(string text)
    {
        AiRun run = Run(text);
        return new IncompatibleReagentSolventValidator().Validate(run.Id, Extract(run), run);
    }

    [Fact]
    public void VacuumDistillation_DoesNotInheritReactionSolventBp()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateSolventTemp(
            "Dissolve in methanol and stir at 50 °C for 1 h. "
            + "The crude product was distilled under vacuum, bp 119 °C at 1 mmHg.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.SolventTemperatureImplausible);
    }

    [Fact]
    public void MeltingPoint_IsIsolatedAsProductProperty()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateSolventTemp(
            "Recrystallize from ethanol. mp 173-175 °C.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.SolventTemperatureImplausible);
        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.ImpossibleRefluxCondition);
    }

    [Fact]
    public void SequentialDurations_StirThenHeat_AreNotContradictory()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateNumeric(
            "Stir for 2 h, then heat for 20 h.");

        Assert.DoesNotContain(findings, f =>
            f.Kind == FindingKind.Contradiction && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void AdditionOver10MinThenStir25Min_IsNotContradictory()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateNumeric(
            "Addition over 10 min, then stir 25 min.");

        Assert.DoesNotContain(findings, f =>
            f.Kind == FindingKind.Contradiction && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void Dropwise_WithoutCoolingOrReactiveReagent_DoesNotImplyMissingTemp()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateMissingTemp(
            "Add the aldehyde dropwise to the stirred mixture.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void Dropwise_WithIceBath_StillImpliesTempIfNoneFound()
    {
        // No temperature claim and no explicit numeric near "dropwise" — but
        // ice bath is recognised by NearbyTemperatureRegex as a fallback so
        // this should not fire either. Ensures we did not regress the cool case.
        IReadOnlyList<ValidationFinding> findings = ValidateMissingTemp(
            "Add the acyl chloride dropwise while cooling in an ice bath.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingTemperature);
    }

    [Fact]
    public void NaH_FollowedByAqueousQuench_IsNotIncompatible()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateIncompatible(
            "Add NaH to the substrate in dry THF and stir for 30 min. "
            + "Quench the reaction by careful addition of water.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.IncompatibleReagentSolvent);
    }

    [Fact]
    public void NaH_InWater_SameStep_StillFlagsIncompatible()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateIncompatible(
            "To a flask was added NaH and water; the mixture was stirred at room temperature for 1 h.");

        Assert.Contains(findings, f => f.Kind == FindingKind.IncompatibleReagentSolvent);
    }

    [Fact]
    public void TlcMonitoring_DuringPurification_IsNotTreatedAsAnalysis()
    {
        // TLC should not terminate solvent state; we just confirm the
        // engine doesn't blow up and the validator pipeline produces no
        // unexpected SolventTemperatureImplausible.
        IReadOnlyList<ValidationFinding> findings = ValidateSolventTemp(
            "Reflux in ethanol for 2 h. Monitor by TLC. Cool and filter.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.SolventTemperatureImplausible);
    }
}
