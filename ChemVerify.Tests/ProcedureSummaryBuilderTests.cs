using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Tests;

public class ProcedureSummaryBuilderTests
{
    [Fact]
    public void EmptyText_ReturnsEmptySummary()
    {
        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(
            "", [], [], new ProceduralContext(false, 0, false, null));

        Assert.False(result.IsProcedural);
        Assert.Empty(result.Steps);
        Assert.Empty(result.Clusters);
        Assert.Empty(result.TopIssues);
    }

    [Fact]
    public void SingleStep_NoClusters()
    {
        string text = "The mixture was cooled to -78 °C and stirred for 2 h";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "-78 °C",
                NormalizedValue = "-78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 0
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "2 h",
                NormalizedValue = "2",
                Unit = "h",
                JsonPayload = """{"contextKey":"time"}""",
                StepIndex = 0
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, claims, ctx);

        Assert.NotEmpty(result.Steps);
        Assert.Empty(result.Clusters);
        Assert.Empty(result.TopIssues);
    }

    [Fact]
    public void MultipleSteps_DifferentTemps_CreatesClusters()
    {
        string text = "Step one: cooled to -78 °C and stirred for 2 h. Step two: heated to 78 °C for 120 min.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "-78 °C",
                NormalizedValue = "-78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 0
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "2 h",
                NormalizedValue = "2",
                Unit = "h",
                JsonPayload = """{"contextKey":"time"}""",
                StepIndex = 0
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "78 °C",
                NormalizedValue = "78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 1
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "120 min",
                NormalizedValue = "120",
                Unit = "min",
                JsonPayload = """{"contextKey":"time"}""",
                StepIndex = 1
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, claims, ctx);

        Assert.Equal(2, result.Clusters.Count);
        Assert.Single(result.TopIssues);
        Assert.Contains("2 condition clusters", result.TopIssues[0].Title);
    }

    [Fact]
    public void BranchingLanguage_ReducesConfidence()
    {
        string text = "Alternatively, the mixture was cooled to -78 °C. In the second route, the mixture was heated to 78 °C.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "-78 °C",
                NormalizedValue = "-78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 0
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "78 °C",
                NormalizedValue = "78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 1
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, claims, ctx);

        Assert.NotEmpty(result.TopIssues);
        TopIssueDto issue = result.TopIssues[0];
        Assert.True(issue.Confidence < 0.7, $"Branching language should reduce confidence below 0.7, got {issue.Confidence}");
        Assert.Contains(issue.Why, w => w.Contains("Branching"));
    }

    [Fact]
    public void StepSummaries_ContainCorrectSnippetsAndClaims()
    {
        string text = "Benzaldehyde was dissolved. Then NaBH4 was added over 2 h.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        // Assign the time claim to the last step
        int lastStepIndex = steps[^1].Index;

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "2 h",
                NormalizedValue = "2",
                Unit = "h",
                JsonPayload = """{"contextKey":"time"}""",
                StepIndex = lastStepIndex
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, claims, ctx);

        Assert.True(result.Steps.Count >= 2, $"Expected >=2 steps, got {result.Steps.Count}");

        StepSummaryDto? stepWithClaim = result.Steps.FirstOrDefault(s => s.Claims.Count > 0);
        Assert.NotNull(stepWithClaim);
        Assert.Equal("2 h", stepWithClaim.Claims[0].RawText);
        Assert.Equal("time", stepWithClaim.Claims[0].ContextKey);
    }

    [Fact]
    public void SameConditions_AllSteps_NoClusters()
    {
        string text = "Cooled to -78 °C. Maintained at -78 °C for 2 h.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "-78 °C",
                NormalizedValue = "-78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 0
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "-78 °C",
                NormalizedValue = "-78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 1
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, claims, ctx);

        Assert.Empty(result.Clusters);
        Assert.Empty(result.TopIssues);
    }

    [Fact]
    public void FormatStepRange_ConsecutiveSteps()
    {
        string result = ProcedureSummaryBuilder.FormatStepRange([0, 1, 2]);
        Assert.Equal("Steps 0\u20132", result);
    }

    [Fact]
    public void FormatStepRange_NonConsecutiveSteps()
    {
        string result = ProcedureSummaryBuilder.FormatStepRange([0, 2, 5]);
        Assert.Equal("Steps 0, 2, 5", result);
    }

    [Fact]
    public void FormatStepRange_SingleStep()
    {
        string result = ProcedureSummaryBuilder.FormatStepRange([3]);
        Assert.Equal("Step 3", result);
    }

    [Fact]
    public void NonConditionClaims_DoNotFormClusters()
    {
        string text = "Added 5 mg NaBH4. Then added 10 mg benzaldehyde.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "5 mg",
                NormalizedValue = "5",
                Unit = "mg",
                JsonPayload = """{"contextKey":"mass"}""",
                StepIndex = 0
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "10 mg",
                NormalizedValue = "10",
                Unit = "mg",
                JsonPayload = """{"contextKey":"mass"}""",
                StepIndex = 1
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, claims, ctx);

        Assert.Empty(result.Clusters);
        Assert.Empty(result.TopIssues);
    }

    [Fact]
    public void ProceduralText_IncreasesConfidence()
    {
        string text = "The reagent was added at -78 °C and stirred for 2 h. Subsequently the mixture was heated to 78 °C for 120 min.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        Assert.True(ctx.IsProcedural, "Expected procedural context from lab-action verbs");

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "-78 °C",
                NormalizedValue = "-78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 0
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "78 °C",
                NormalizedValue = "78",
                Unit = "°C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 1
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, claims, ctx);

        Assert.NotEmpty(result.TopIssues);
        TopIssueDto issue = result.TopIssues[0];
        Assert.Contains(issue.Why, w => w.Contains("procedural"));
    }

    [Fact]
    public void StepRoles_AnnotatedWhenProvided()
    {
        string text =
            "NaBH4 was added portionwise at 0 °C and stirred for 30 min.\n\n" +
            "### References\n" +
            "1. Smith, J. et al. J. Org. Chem. 2020.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(
            text, steps, ctx.ReferencesStartOffset);

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, [], ctx, roles);

        Assert.True(result.Steps.Count >= 2, $"Expected >=2 steps, got {result.Steps.Count}");
        Assert.Contains(result.Steps, s => s.Role == "Procedure");
        Assert.Contains(result.Steps, s => s.Role == "Reference");
    }

    [Fact]
    public void StepRoles_NullWhenNotProvided()
    {
        string text = "NaBH4 was added. The mixture was stirred.";
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(text, steps, [], ctx);

        Assert.All(result.Steps, s => Assert.Null(s.Role));
    }
}
