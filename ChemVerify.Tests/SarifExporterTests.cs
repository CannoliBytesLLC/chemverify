using System.Text.Json;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Abstractions.Validation;
using ChemVerify.Core.Reporting;

namespace ChemVerify.Tests;

public class SarifExporterTests
{
    [Fact]
    public void Build_WithMetadataAndText_EmitsRulesResultsAndRegions()
    {
        string inputText = "First line\nSecond line\n";
        int startOffset = inputText.IndexOf("Second", StringComparison.Ordinal);
        int endOffset = startOffset + "Second".Length;

        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = nameof(TestValidator),
                RuleId = "TEST_RULE",
                Status = ValidationStatus.Fail,
                Message = "Test error",
                Confidence = 0.8,
                EvidenceStartOffset = startOffset,
                EvidenceEndOffset = endOffset,
                EvidenceSnippet = "Second"
            }
        ];

        ReportDto report = new()
        {
            EngineVersion = "1.0.0",
            RuleSetVersion = "1.0.0"
        };

        string json = SarifExporter.Build(
            report,
            findings,
            "input.txt",
            inputText,
            new IValidator[] { new TestValidator() });

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        JsonElement run = root.GetProperty("runs")[0];

        JsonElement rule = run.GetProperty("tool").GetProperty("driver")
            .GetProperty("rules")[0];
        Assert.Equal("TEST_RULE", rule.GetProperty("id").GetString());
        Assert.Equal(nameof(TestValidator), rule.GetProperty("name").GetString());
        Assert.Equal("Test validator.", rule.GetProperty("shortDescription").GetProperty("text").GetString());
        Assert.Equal("error", rule.GetProperty("defaultConfiguration").GetProperty("level").GetString());

        JsonElement result = run.GetProperty("results")[0];
        Assert.Equal("TEST_RULE", result.GetProperty("ruleId").GetString());
        Assert.Equal("error", result.GetProperty("level").GetString());
        Assert.Equal("Test error", result.GetProperty("message").GetProperty("text").GetString());

        JsonElement region = result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region");
        Assert.Equal(2, region.GetProperty("startLine").GetInt32());
        Assert.Equal(1, region.GetProperty("startColumn").GetInt32());
        Assert.Equal("Second", region.GetProperty("snippet").GetProperty("text").GetString());
    }

    [Fact]
    public void Build_WithoutText_UsesCharOffsets()
    {
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = nameof(TestValidator),
                RuleId = "TEST_RULE",
                Status = ValidationStatus.Unverified,
                Message = "Test warning",
                Confidence = 0.5,
                EvidenceStartOffset = 5,
                EvidenceEndOffset = 10
            }
        ];

        ReportDto report = new()
        {
            EngineVersion = "1.0.0",
            RuleSetVersion = "1.0.0"
        };

        string json = SarifExporter.Build(report, findings, "input.txt");

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement region = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region");

        Assert.Equal(5, region.GetProperty("charOffset").GetInt32());
        Assert.Equal(5, region.GetProperty("charLength").GetInt32());
    }

    [ValidatorMetadata(
        Id = "TEST_RULE",
        Kind = "TEST",
        DefaultSeverity = Severity.High,
        Description = "Test validator.")]
    private sealed class TestValidator : IValidator
    {
        public IReadOnlyList<ValidationFinding> Validate(Guid runId, IReadOnlyList<ExtractedClaim> claims, AiRun run)
            => [];
    }
}
