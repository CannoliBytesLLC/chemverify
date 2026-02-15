using ChemVerify.Core.Services;

namespace ChemVerify.Tests;

public class StepSegmenterTests
{
    [Fact]
    public void EmptyText_ReturnsNoSteps()
    {
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment("");
        Assert.Empty(steps);
    }

    [Fact]
    public void SingleSentence_ReturnsSingleStep()
    {
        string text = "Benzaldehyde was dissolved in MeOH";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        Assert.Single(steps);
        Assert.Equal(0, steps[0].Index);
    }

    [Fact]
    public void PeriodSeparated_ReturnsTwoSteps()
    {
        string text = "Step one was done. Step two followed.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        Assert.True(steps.Count >= 2, $"Expected >=2 steps, got {steps.Count}");
        Assert.Equal(0, steps[0].Index);
        Assert.Equal(1, steps[1].Index);
    }

    [Fact]
    public void SemicolonSeparated_SplitsSteps()
    {
        string text = "Added reagent; stirred for 30 min; filtered";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        Assert.True(steps.Count >= 2, $"Expected >=2 steps, got {steps.Count}");
    }

    [Fact]
    public void NewlineSeparated_SplitsSteps()
    {
        string text = "Step one\nStep two\nStep three";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        Assert.True(steps.Count >= 3, $"Expected >=3 steps, got {steps.Count}");
    }

    [Fact]
    public void ThenTransition_SplitsSteps()
    {
        string text = "Benzaldehyde was dissolved in MeOH, then NaBH4 was added";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        Assert.True(steps.Count >= 2,
            $"Expected >=2 steps from 'then' boundary, got {steps.Count}");
    }

    [Fact]
    public void GetStepIndex_ReturnsCorrectStep()
    {
        string text = "Step one. Step two.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        Assert.True(steps.Count >= 2);

        int? step0 = StepSegmenter.GetStepIndex(steps, 0); // "S" in "Step one"
        Assert.Equal(0, step0);

        int? step1 = StepSegmenter.GetStepIndex(steps, steps[1].StartOffset + 1);
        Assert.Equal(1, step1);
    }
}
