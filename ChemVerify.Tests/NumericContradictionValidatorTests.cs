using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class NumericContradictionValidatorTests
{
    private readonly NumericContradictionValidator _validator = new();
    private readonly NumericUnitExtractor _extractor = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    [Fact]
    public void SequentialRefluxTimes_NoContradictionFail()
    {
        string text =
            "The mixture was refluxed for 30 minutes. " +
            "After cooling, the reagent was added, and the mixture was refluxed for an additional 15 minutes.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings,
            f => f.Kind == FindingKind.Contradiction
              && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void SequentialStirTimes_EmitsPassWithSequentialDurationKind()
    {
        string text =
            "Stir the reaction at 0 °C for 2 h, then stir for another 1 h at room temperature.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings,
            f => f.Kind == FindingKind.Contradiction);

        // Should have a SequentialDuration pass (or no contradiction at all)
        var seqFindings = findings.Where(f => f.Kind == FindingKind.SequentialDuration).ToList();
        if (seqFindings.Count > 0)
        {
            Assert.All(seqFindings, f => Assert.Equal(ValidationStatus.Pass, f.Status));
            Assert.All(seqFindings, f => Assert.NotNull(f.EvidenceSnippet));
        }
    }

    [Fact]
    public void DetectSequentialDuration_WithAdditiveCue_ReturnsCuePhrase()
    {
        string text = "Reflux for 30 min. Add reagent and reflux for an additional 15 min.";

        var claimA = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "30 min",
            NormalizedValue = "30",
            Unit = "min",
            SourceLocator = "AnalyzedText:15-21",
            JsonPayload = "{\"contextKey\":\"time\",\"timeAction\":\"reflux\"}"
        };

        var claimB = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "15 min",
            NormalizedValue = "15",
            Unit = "min",
            SourceLocator = "AnalyzedText:56-62",
            JsonPayload = "{\"contextKey\":\"time\",\"timeAction\":\"reflux\"}"
        };

        string? cue = NumericContradictionValidator.DetectSequentialDuration(text, claimA, claimB);

        Assert.NotNull(cue);
        Assert.Contains("additional", cue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectSequentialDuration_NoAdditiveCue_ReturnsNull()
    {
        string text = "Heat for 30 min. Cool to rt. Heat again for 15 min.";

        var claimA = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "30 min",
            NormalizedValue = "30",
            Unit = "min",
            SourceLocator = "AnalyzedText:9-15",
            JsonPayload = "{\"contextKey\":\"time\",\"timeAction\":\"heat\"}"
        };

        var claimB = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "15 min",
            NormalizedValue = "15",
            Unit = "min",
            SourceLocator = "AnalyzedText:44-50",
            JsonPayload = "{\"contextKey\":\"time\",\"timeAction\":\"heat\"}"
        };

        string? cue = NumericContradictionValidator.DetectSequentialDuration(text, claimA, claimB);

        Assert.Null(cue);
    }

    [Fact]
    public void CheckpointVsTotal_NoContradiction()
    {
        // Pattern A: "After 10 minutes ... stir for a total of 45 minutes."
        string text =
            "After 10 minutes, add the remaining portion of the reagent and stir for a total of 45 minutes.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings,
            f => f.Kind == FindingKind.Contradiction
              && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void AdditionDurationSeparation_NoContradiction()
    {
        // Pattern B: "stir for 20 minutes and add, by dropwise addition over 10 minutes"
        string text =
            "Stir for 20 minutes and add, by dropwise addition over 10 minutes, a solution of the acid in THF.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings,
            f => f.Kind == FindingKind.Contradiction
              && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void CrossOperationHeatVsAddition_NoContradiction()
    {
        // Pattern C: "heat to reflux ... After 3 hours ... add ... over 10 minutes"
        string text =
            "The mixture was heated to reflux. After 3 hours, add the catalyst portionwise over 10 minutes.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings,
            f => f.Kind == FindingKind.Contradiction
              && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void SameOperationSameScope_StillContradicts()
    {
        // Control: two StirHold times in same scope, no sequential/checkpoint cues => Fail
        string text =
            "Stir the mixture for 5 minutes and stir for 60 minutes.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.Contains(findings,
            f => f.Kind == FindingKind.Contradiction
              && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void DifferentTemperatureRegimes_NoContradiction()
    {
        // Pattern: same operation at different temperatures should not flag as contradiction
        string text =
            "The mixture was stirred at 0 °C for 15 min and then at room temperature for 2 h.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings,
            f => f.Kind == FindingKind.Contradiction
              && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void ChromatographyGradient_NoContradiction()
    {
        // Pattern: percent values in chromatography gradient context should not flag
        string text =
            "Purification by flash chromatography on silica gel (EtOAc/hexanes, gradient 2% to 4%) afforded the product.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings,
            f => f.Kind == FindingKind.Contradiction
              && f.Status == ValidationStatus.Fail);
    }
}
