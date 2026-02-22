using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class DryInertMismatchValidatorTests
{
    private readonly DryInertMismatchValidator _validator = new();
    private readonly ReagentRoleExtractor _extractor = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    [Fact]
    public void DryIceTrap_WithAqueousWorkup_NoFail()
    {
        string text =
            "The reaction was carried out using a dry-ice/acetone trap to collect volatiles. " +
            "After completion, add deionized water (50 mL) to the residue. " +
            "Transfer to a separatory funnel and extract with ethyl acetate (3 x 30 mL). " +
            "Combine the organic layers and wash the aqueous layer with brine.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.AmbiguousWorkupTransition);
    }

    [Fact]
    public void DryIceTrapOnly_DoesNotEstablishDryConditions()
    {
        // "dry ice" should not count as dryness. Even though "dry" matches
        // DrynessRegex in the extractor, the validator should exclude it.
        string text =
            "Vapors were collected with a dry ice trap. " +
            "The residue was dissolved in water and stirred.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.AmbiguousWorkupTransition);
    }

    [Fact]
    public void TrueDryConditions_AqueousWithWorkup_NoFail()
    {
        // Aqueous introduction with clear workup cues should not trigger
        string text =
            "Under anhydrous conditions and nitrogen atmosphere, " +
            "the Grignard reagent was prepared. " +
            "The reaction was quenched with water and extracted with ethyl acetate.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.AmbiguousWorkupTransition);
    }

    [Fact]
    public void TrueDryConditions_AqueousNoWorkupCues_Fails()
    {
        // Deliberately avoid any workup language
        string text =
            "Under anhydrous conditions and nitrogen atmosphere, " +
            "the Grignard reagent was prepared in dry THF (50 mL). " +
            "To the mixture was added 10 mL of water and the product precipitated.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.Contains(findings, f => f.Kind == FindingKind.AmbiguousWorkupTransition);
    }

    [Fact]
    public void DilutedWithWaterUnderArgon_WorkupDetected_NoFail()
    {
        // "diluted with water" is a recognized workup transition
        string text =
            "The reaction was carried out under argon in anhydrous DMF. " +
            "After stirring for 12 h, the mixture was diluted with water (100 mL) and extracted with DCM (3 × 50 mL).";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.AmbiguousWorkupTransition);
        Assert.Contains(findings, f => f.Kind == FindingKind.WorkupTransitionDetected
                                    && f.Status == ValidationStatus.Pass);
    }
}
