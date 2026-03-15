using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Evaluation;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Evaluation;

namespace ChemVerify.Tests;

public class EvaluationFrameworkTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub validator that always emits one finding with the given status and confidence.
    /// </summary>
    private sealed class StubValidator(
        string name,
        ValidationStatus status,
        double confidence,
        string? kind = null) : IValidator
    {
        public IReadOnlyList<ValidationFinding> Validate(
            Guid runId, IReadOnlyList<ExtractedClaim> claims, AiRun run)
            => [new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = name,
                Status = status,
                Confidence = confidence,
                Kind = kind,
                Message = $"Stub finding from {name}"
            }];
    }

    /// <summary>
    /// Stub validator that emits no findings (silent pass).
    /// </summary>
    private sealed class SilentValidator(string name) : IValidator
    {
        public IReadOnlyList<ValidationFinding> Validate(
            Guid runId, IReadOnlyList<ExtractedClaim> claims, AiRun run) => [];
    }

    /// <summary>
    /// Stub extractor that returns no claims (validators are tested in isolation).
    /// </summary>
    private sealed class EmptyExtractor : IClaimExtractor
    {
        public IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text) => [];
    }

    private static GoldSetItem MakeGoldItem(
        string id,
        string text,
        IReadOnlyList<ExpectedFinding> expected,
        IReadOnlyList<string>? domainTags = null,
        string? sourceDataset = null) => new(
            Id: id,
            ParagraphText: text,
            ExpectedFindings: expected,
            ReviewerLabel: "test",
            ReviewedAtUtc: DateTimeOffset.UtcNow,
            DomainTags: domainTags,
            SourceDataset: sourceDataset);

    private static ValidatorAuditRunner CreateRunner(
        IEnumerable<IValidator> validators,
        IClaimExtractor? extractor = null)
    {
        var ext = extractor ?? new EmptyExtractor();
        return new ValidatorAuditRunner(
            validators,
            ext,
            new ConfidenceCalibrationAnalyzer(),
            new ReviewQueueBuilder(),
            new StratificationAnalyzer());
    }

    // ── Confusion matrix tests ──────────────────────────────────────────────

    [Fact]
    public void WhenValidatorCorrectlyFails_ThenTruePositiveCounted()
    {
        // Arrange: validator emits Fail, gold-set expects Fail
        var validators = new IValidator[]
        {
            new StubValidator("TestValidator", ValidationStatus.Fail, 0.9)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "sample text",
            [
                new ExpectedFinding("TestValidator", ValidationStatus.Fail)
            ])
        };

        // Act
        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        // Assert
        ValidatorAuditResult result = Assert.Single(summary.ValidatorResults);
        Assert.Equal("TestValidator", result.ValidatorName);
        Assert.Equal(1, result.Matrix.TruePositives);
        Assert.Equal(0, result.Matrix.FalsePositives);
        Assert.Equal(0, result.Matrix.FalseNegatives);
    }

    [Fact]
    public void WhenValidatorFalseAlarm_ThenFalsePositiveCounted()
    {
        // Arrange: validator emits Fail, gold-set expects Pass
        var validators = new IValidator[]
        {
            new StubValidator("TestValidator", ValidationStatus.Fail, 0.8)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "clean procedure",
            [
                new ExpectedFinding("TestValidator", ValidationStatus.Pass)
            ])
        };

        // Act
        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        // Assert
        ValidatorAuditResult result = Assert.Single(summary.ValidatorResults);
        Assert.Equal(0, result.Matrix.TruePositives);
        Assert.Equal(1, result.Matrix.FalsePositives);
    }

    [Fact]
    public void WhenValidatorMissesIssue_ThenFalseNegativeCounted()
    {
        // Arrange: validator is silent, gold-set expects Fail
        var validators = new IValidator[]
        {
            new SilentValidator("TestValidator")
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "problematic text",
            [
                new ExpectedFinding("TestValidator", ValidationStatus.Fail)
            ])
        };

        // Act
        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        // Assert
        ValidatorAuditResult result = Assert.Single(summary.ValidatorResults);
        Assert.Equal(0, result.Matrix.TruePositives);
        Assert.Equal(1, result.Matrix.FalseNegatives);
    }

    // ── Leaderboard tests ───────────────────────────────────────────────────

    [Fact]
    public void WhenMultipleValidators_ThenLeaderboardRankedByF1()
    {
        // Arrange: one perfect validator, one that always false-alarms
        var validators = new IValidator[]
        {
            new StubValidator("GoodValidator", ValidationStatus.Fail, 0.9),
            new StubValidator("BadValidator", ValidationStatus.Fail, 0.5)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "text",
            [
                new ExpectedFinding("GoodValidator", ValidationStatus.Fail),
                new ExpectedFinding("BadValidator", ValidationStatus.Pass)
            ])
        };

        // Act
        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        // Assert
        Assert.Equal(2, summary.Leaderboard.Count);
        Assert.Equal("GoodValidator", summary.Leaderboard[0].ValidatorName);
        Assert.Equal(1, summary.Leaderboard[0].Rank);
    }

    // ── Confidence calibration tests ────────────────────────────────────────

    [Fact]
    public void WhenTruePositive_ThenAvgConfidenceTruePositivesPopulated()
    {
        var validators = new IValidator[]
        {
            new StubValidator("V1", ValidationStatus.Fail, 0.85)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "text",
            [
                new ExpectedFinding("V1", ValidationStatus.Fail)
            ])
        };

        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        ValidatorAuditResult result = Assert.Single(summary.ValidatorResults);
        Assert.NotNull(result.Calibration.AvgConfidenceTruePositives);
        Assert.Equal(0.85, result.Calibration.AvgConfidenceTruePositives!.Value, 2);
    }

    // ── Stratification tests ────────────────────────────────────────────────

    [Fact]
    public void WhenDomainTagsPresent_ThenStratifiedByDomainTag()
    {
        var validators = new IValidator[]
        {
            new StubValidator("V1", ValidationStatus.Fail, 0.8)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "organometallic procedure",
            [
                new ExpectedFinding("V1", ValidationStatus.Fail)
            ],
            domainTags: ["organometallic"]),
            MakeGoldItem("GS-2", "esterification procedure",
            [
                new ExpectedFinding("V1", ValidationStatus.Fail)
            ],
            domainTags: ["esterification"])
        };

        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        var domainStrats = summary.StratifiedResults
            .Where(s => s.Key.Dimension == "DomainTag")
            .ToList();
        Assert.Equal(2, domainStrats.Count);
        Assert.Contains(domainStrats, s => s.Key.Value == "organometallic");
        Assert.Contains(domainStrats, s => s.Key.Value == "esterification");
    }

    [Fact]
    public void WhenSourceDatasetPresent_ThenStratifiedBySource()
    {
        var validators = new IValidator[]
        {
            new StubValidator("V1", ValidationStatus.Fail, 0.7)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "text",
            [
                new ExpectedFinding("V1", ValidationStatus.Fail)
            ],
            sourceDataset: "pistachio")
        };

        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        Assert.Contains(summary.StratifiedResults,
            s => s.Key.Dimension == "SourceDataset" && s.Key.Value == "pistachio");
    }

    // ── Review queue tests ──────────────────────────────────────────────────

    [Fact]
    public void WhenConflictingSignals_ThenQueuedForReview()
    {
        // One validator passes, another fails on the same item
        var validators = new IValidator[]
        {
            new StubValidator("PassingValidator", ValidationStatus.Pass, 0.9),
            new StubValidator("FailingValidator", ValidationStatus.Fail, 0.8)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "ambiguous text",
            [
                new ExpectedFinding("PassingValidator", ValidationStatus.Pass),
                new ExpectedFinding("FailingValidator", ValidationStatus.Fail)
            ])
        };

        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);

        Assert.NotEmpty(summary.ReviewQueue);
        Assert.True(summary.ReviewQueue[0].HasConflictingSignals);
    }

    // ── Report export tests ─────────────────────────────────────────────────

    [Fact]
    public void WhenExportJson_ThenValidJson()
    {
        var validators = new IValidator[]
        {
            new StubValidator("V1", ValidationStatus.Fail, 0.9)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "text",
            [
                new ExpectedFinding("V1", ValidationStatus.Fail)
            ])
        };

        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);
        var exporter = new EvaluationReportExporter();

        string json = exporter.ExportJson(summary);

        Assert.NotEmpty(json);
        Assert.Contains("validatorResults", json);
        Assert.Contains("leaderboard", json);
    }

    [Fact]
    public void WhenExportCsv_ThenContainsExpectedFiles()
    {
        var validators = new IValidator[]
        {
            new StubValidator("V1", ValidationStatus.Fail, 0.9)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "text",
            [
                new ExpectedFinding("V1", ValidationStatus.Fail)
            ])
        };

        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);
        var exporter = new EvaluationReportExporter();

        Dictionary<string, string> csvFiles = exporter.ExportCsv(summary);

        Assert.True(csvFiles.ContainsKey("leaderboard.csv"));
        Assert.True(csvFiles.ContainsKey("validator_metrics.csv"));
        Assert.True(csvFiles.ContainsKey("confidence_calibration.csv"));
        Assert.True(csvFiles.ContainsKey("stratified_metrics.csv"));
        Assert.True(csvFiles.ContainsKey("review_queue.csv"));
    }

    [Fact]
    public void WhenExportMarkdown_ThenContainsLeaderboardTable()
    {
        var validators = new IValidator[]
        {
            new StubValidator("V1", ValidationStatus.Fail, 0.9)
        };
        var goldSet = new[]
        {
            MakeGoldItem("GS-1", "text",
            [
                new ExpectedFinding("V1", ValidationStatus.Fail)
            ])
        };

        EvaluationSummary summary = CreateRunner(validators).Evaluate(goldSet);
        var exporter = new EvaluationReportExporter();

        string markdown = exporter.ExportMarkdown(summary);

        Assert.Contains("## Validator Leaderboard", markdown);
        Assert.Contains("V1", markdown);
    }
}
