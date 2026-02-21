using ChemVerify.Core.Services;

namespace ChemVerify.Tests;

public class ProceduralContextDetectorTests
{
    [Fact]
    public void NarrativeText_NotProcedural()
    {
        string text =
            "Sodium borohydride (NaBH4) is one of the most commonly used reducing agents in organic chemistry. " +
            "It was first described by Schlesinger and Brown in 1953. " +
            "L-Selectride offers improved selectivity for sterically demanding ketones.";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.False(ctx.IsProcedural);
    }

    [Fact]
    public void ProceduralText_WithLabVerbs_IsProcedural()
    {
        string text =
            "Benzaldehyde (1.06 g, 10 mmol) was dissolved in MeOH (20 mL). " +
            "NaBH4 (0.38 g, 10 mmol) was added portionwise at 0 °C. " +
            "The mixture was stirred for 30 min.";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.True(ctx.IsProcedural);
        Assert.True(ctx.HasLabActionVerbs);
    }

    [Fact]
    public void TextWithManySteps_IsProcedural_EvenWithoutLabVerbs()
    {
        string text =
            "Step one overview.\n" +
            "Step two overview.\n" +
            "Step three overview.\n" +
            "Step four overview.";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.True(ctx.IsProcedural);
        Assert.True(ctx.StepCount >= 4);
    }

    [Fact]
    public void EmptyText_NotProcedural()
    {
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment("");
        ProceduralContext ctx = ProceduralContextDetector.Detect("", steps);

        Assert.False(ctx.IsProcedural);
        Assert.Equal(0, ctx.StepCount);
    }

    [Fact]
    public void ReferencesSection_Detected()
    {
        string text =
            "The reaction proceeded smoothly.\n" +
            "References\n" +
            "1. Smith, J. et al. J. Org. Chem. 2020.\n";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.NotNull(ctx.ReferencesStartOffset);
        Assert.True(ctx.ReferencesStartOffset < text.Length);
    }

    [Fact]
    public void MarkdownReferencesHeading_Detected()
    {
        string text =
            "Procedure text here.\n" +
            "### References\n" +
            "DOI:10.1234/test\n";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.NotNull(ctx.ReferencesStartOffset);
    }

    [Fact]
    public void NoReferencesSection_NullOffset()
    {
        string text = "NaBH4 was added. The mixture was stirred.";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.Null(ctx.ReferencesStartOffset);
    }

    [Fact]
    public void NarrativeWithSingleLabVerb_NotProcedural()
    {
        string text =
            "The Grignard reagent is prepared under strictly anhydrous conditions " +
            "to prevent quenching by atmospheric moisture. " +
            "This is a fundamental method for increasing carbon chain complexity.";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.False(ctx.IsProcedural);
    }

    [Fact]
    public void ProceduralTextWithVerbsAndQuantities_IsProcedural()
    {
        string text =
            "NaBH4 (0.38 g, 10 mmol) was added portionwise. " +
            "The mixture was stirred at 0 \u00b0C for 30 min.";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.True(ctx.IsProcedural);
    }

    [Fact]
    public void NarrativeWithVerbsAndQuantitiesAndHedges_NotProcedural()
    {
        string text =
            "It has been described that the mixture was stirred for 2 h " +
            "and heated to 80 \u00b0C in prior work. " +
            "This approach was previously reported by Smith et al.";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.False(ctx.IsProcedural);
    }
}
