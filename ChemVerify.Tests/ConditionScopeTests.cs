using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Procedure;
using ChemVerify.Core.Validators.State;

namespace ChemVerify.Tests;

/// <summary>
/// Behavioral tests for scoped condition binding and phase-aware state
/// resets — guards against the Pistachio false-positive class where reflux
/// state, room-temperature claims, or product-property temperatures leak
/// across procedural steps.
/// </summary>
public class ConditionScopeTests
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
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        return new SolventTemperatureValidator().Validate(run.Id, claims, run);
    }

    private static IReadOnlyList<ValidationFinding> ValidateOrdering(string text)
    {
        AiRun run = Run(text);
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        return new ProceduralOrderingValidator().Validate(run.Id, claims, run);
    }

    [Fact]
    public void RoomTempStir_ThenReflux_IsNotRefluxAt25C()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateSolventTemp(
            "Dissolve in ethanol. Stir at room temperature overnight, then heat to reflux for 1 hour.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.ImpossibleRefluxCondition);
    }

    [Fact]
    public void RefluxFollowedByMeltingPoint_DoesNotTreatMpAsRefluxTemperature()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateSolventTemp(
            "Dissolve in ethanol and reflux for 29 hours. Cool in an ice bath and filter. mp 165-175 °C.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.ImpossibleRefluxCondition);
        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.SolventTemperatureImplausible);
    }

    [Fact]
    public void VacuumDistillationBp_DoesNotTriggerSolventBpViolation()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateSolventTemp(
            "The product was dissolved in methanol. bp 91-103 °C / 5 mm Hg.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.SolventTemperatureImplausible);
    }

    [Fact]
    public void WorkupThenExtraction_IsNotPhaseRegression()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateOrdering(
            "The reaction was quenched with saturated NH4Cl. "
            + "Wash the aqueous layer; extract with methylene chloride and combine the organic layers.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.ProceduralOrderingAnomaly);
    }

    [Fact]
    public void MultiStageReflux_CoolThenRefluxAgain_IsAllowed()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateOrdering(
            "Dissolve in ethanol and heat under reflux for 2 h. "
            + "Cool to room temperature. Then heat to reflux again for 1 h.");

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.ProceduralOrderingAnomaly);
    }

    [Fact]
    public void ExplicitRefluxAt25C_SameClause_StillReportsImpossibleReflux()
    {
        IReadOnlyList<ValidationFinding> findings = ValidateSolventTemp(
            "Dissolve in toluene and reflux at 25 °C for 1 h.");

        Assert.Contains(findings, f => f.Kind == FindingKind.ImpossibleRefluxCondition);
    }
}
