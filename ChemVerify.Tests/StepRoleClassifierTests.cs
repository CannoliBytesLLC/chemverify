using ChemVerify.Core.Services;

namespace ChemVerify.Tests;

public class StepRoleClassifierTests
{
    [Fact]
    public void ProceduralStep_ClassifiedAsProcedure()
    {
        string text = "NaBH4 (0.38 g, 10 mmol) was added portionwise and stirred for 30 min.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, referencesStartOffset: null);

        Assert.All(roles, kv => Assert.Equal(StepRole.Procedure, kv.Value));
    }

    [Fact]
    public void QuestionWithNoLabVerbs_ClassifiedAsQuestionOrPrompt()
    {
        string text = "Would you like to explore perhaps a different multistep route involving Grignard reagents?";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, referencesStartOffset: null);

        Assert.All(roles, kv => Assert.Equal(StepRole.QuestionOrPrompt, kv.Value));
    }

    [Fact]
    public void QuestionWithLabVerbs_ClassifiedAsProcedure()
    {
        // Even though there's a "?", the presence of lab-action verbs makes it procedural
        string text = "Was the mixture stirred and heated at 80 °C for 2 h?";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, referencesStartOffset: null);

        Assert.All(roles, kv => Assert.Equal(StepRole.Procedure, kv.Value));
    }

    [Fact]
    public void ReferenceSection_ClassifiedAsReference()
    {
        string text = "The mixture was stirred for 2 h.\n\nReferences:\n1. Smith et al. DOI: 10.1021/test";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, ctx.ReferencesStartOffset);

        bool hasProcedure = roles.Values.Any(r => r == StepRole.Procedure);
        Assert.True(hasProcedure, "Should have at least one Procedure step");
        // Any step at or beyond the references boundary should be Reference
        if (ctx.ReferencesStartOffset.HasValue)
        {
            int refOffset = ctx.ReferencesStartOffset.Value;
            foreach (TextStep step in steps)
            {
                if (step.StartOffset >= refOffset)
                    Assert.Equal(StepRole.Reference, roles[step.Index]);
            }
        }
    }

    [Fact]
    public void NarrativeWithoutLabVerbs_ClassifiedAsNarrative()
    {
        string text = "This synthesis is commonly used in pharmaceutical manufacturing.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, referencesStartOffset: null);

        Assert.All(roles, kv => Assert.Equal(StepRole.Narrative, kv.Value));
    }

    [Fact]
    public void MixedSteps_CorrectlyClassified()
    {
        string text =
            "The aldehyde was dissolved in THF and stirred for 1 h at 25 °C.\n\n" +
            "Would you like to try an alternative route?";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, referencesStartOffset: null);

        Assert.Contains(roles, kv => kv.Value == StepRole.Procedure);
        bool hasNonProcedure = roles.Values.Any(r => r is StepRole.Narrative or StepRole.QuestionOrPrompt);
        Assert.True(hasNonProcedure, "Expected at least one non-procedural step");
    }

    [Fact]
    public void MeasuredQuantityAlone_ClassifiedAsProcedure()
    {
        // No lab verbs, but has measured quantities — should still be Procedure
        string text = "Benzaldehyde (1.06 g, 10 mmol) in 10 mL of MeOH.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, referencesStartOffset: null);

        Assert.All(roles, kv => Assert.Equal(StepRole.Procedure, kv.Value));
    }
}
