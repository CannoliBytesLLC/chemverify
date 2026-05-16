using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Procedure;
using ChemVerify.Core.Validators.State;

namespace ChemVerify.Tests;

public class ProcedureStateEngineTests
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
        List<ExtractedClaim> all = [.. reagent.Extract(run.Id, run.InputText!), .. numeric.Extract(run.Id, run.InputText!)];
        return all;
    }

    [Fact]
    public void Build_ProducesOneSnapshotPerStep()
    {
        AiRun run = Run("Add THF (10 mL). Cool to 0 °C. Stir for 1 h.");
        IReadOnlyList<StateSnapshot> snaps = ProcedureStateEngine.Build(run.InputText!, Extract(run));

        Assert.True(snaps.Count >= 3);
        Assert.Equal(0, snaps[0].StepIndex);
    }

    [Fact]
    public void AtmosphereContainment_NitrogenThenOpenBeaker_Fails()
    {
        AiRun run = Run("The reaction was performed under nitrogen. The mixture was poured into an open beaker and stirred.");
        AtmosphereConsistencyValidator v = new();

        IReadOnlyList<ValidationFinding> findings = v.Validate(run.Id, Extract(run), run);

        Assert.Contains(findings, f => f.Kind == FindingKind.AtmosphereContainmentIssue);
    }

    [Fact]
    public void DrynessSemanticContradiction_DriedWithWater_Fails()
    {
        AiRun run = Run("The organic layer was dried with water and concentrated.");
        DrynessConsistencyValidator v = new();

        IReadOnlyList<ValidationFinding> findings = v.Validate(run.Id, Extract(run), run);

        Assert.Contains(findings, f => f.Kind == FindingKind.DrynessSemanticContradiction);
    }
}
