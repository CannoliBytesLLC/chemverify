using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Tests;

public class ReportBuilderTests
{
    [Fact]
    public void Verdict_OnlyTextIntegrityFindings_ReturnsCleanupRecommendation()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C without numeric value.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken,
                RuleId = "MALFORMED_CHEMICAL_TOKEN"
            }
        ];

        var report = ReportBuilder.Build(0.10, claims, findings,
            policyProfileName: "ScientificTextV0",
            policyProfileVersion: "2025.1");

        Assert.Equal("Scientific writing/format issues detected. Manual cleanup recommended.", report.Verdict);

        // Provenance — report level
        Assert.Equal("ScientificTextV0", report.PolicyProfileName);
        Assert.Equal("2025.1", report.PolicyProfileVersion);

        // Provenance — finding level (pre-populated RuleId preserved)
        Assert.Equal("MALFORMED_CHEMICAL_TOKEN", findings[0].RuleId);
        Assert.Equal(EngineVersionProvider.RuleSetVersion, findings[0].RuleVersion);
    }

    [Fact]
    public void Verdict_ContradictionAndTextIntegrity_ReturnsInconsistencyWarning()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "NumericContradictionValidator",
                Status = ValidationStatus.Fail,
                Message = "Possible contradiction: 82% vs 15%.",
                Confidence = 0.9,
                Kind = FindingKind.Contradiction
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken
            }
        ];

        var report = ReportBuilder.Build(0.50, claims, findings);

        Assert.Equal("Internal inconsistencies detected. Manual review recommended before relying on this output.", report.Verdict);
    }

    [Fact]
    public void Verdict_MultiScenarioOnly_ReturnsRegimesDetected()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "NumericContradictionValidator",
                Status = ValidationStatus.Unverified,
                Message = "Multiple scenarios detected for temp.",
                Confidence = 0.7,
                Kind = FindingKind.MultiScenario
            }
        ];

        var report = ReportBuilder.Build(0.05, claims, findings);

        Assert.Equal("Internally consistent; multiple distinct experimental regimes detected.", report.Verdict);
    }

    [Fact]
    public void Verdict_NoAttention_ReturnsConsistent()
    {
        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.CitationDoi,
                RawText = "10.1021/test",
                NormalizedValue = "10.1021/test"
            }
        ];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "DoiFormatValidator",
                Status = ValidationStatus.Pass,
                Message = "DOI format is valid.",
                Confidence = 1.0
            }
        ];

        var report = ReportBuilder.Build(0.0, claims, findings,
            policyProfileName: "StrictChemistryV0");

        Assert.Equal("No internal inconsistencies detected. Extracted claims are well-formed and mutually consistent.", report.Verdict);

        // Provenance — report level
        Assert.False(string.IsNullOrWhiteSpace(report.EngineVersion));
        Assert.Equal(EngineVersionProvider.RuleSetVersion, report.RuleSetVersion);
        Assert.Equal("StrictChemistryV0", report.PolicyProfileName);
        Assert.Equal(EngineVersionProvider.RuleSetVersion, report.PolicyProfileVersion);

        // Provenance — finding level (back-filled from ValidatorName)
        var finding = findings[0];
        Assert.Equal("DoiFormatValidator", finding.RuleId);
        Assert.Equal(EngineVersionProvider.RuleSetVersion, finding.RuleVersion);
    }

    [Fact]
    public void SuggestionPayload_RenderedInAttention()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C without numeric value.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken,
                JsonPayload = "{\"expected\":\"temperature numeric value\",\"examples\":[\"0 \\u00b0C\",\"25 \\u00b0C\",\"-78 \\u00b0C\"],\"token\":\"\\u00b0C\"}"
            }
        ];

        var report = ReportBuilder.Build(0.10, claims, findings);

        Assert.True(
            report.Attention.Any(a => a.Contains("Expected:") && a.Contains("temperature numeric value")),
            "Attention should contain a suggestion line from JsonPayload.");
        Assert.True(
            report.Attention.Any(a => a.Contains("0 °C") && a.Contains("25 °C") && a.Contains("-78 °C")),
            "Suggestion line should list example values.");
    }

    [Fact]
    public void InvalidJsonPayload_DoesNotThrow()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C without numeric value.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken,
                JsonPayload = "{not json"
            }
        ];

        var report = ReportBuilder.Build(0.10, claims, findings);

        Assert.NotNull(report);
        Assert.Single(report.Attention); // Finding line only, no suggestion
        Assert.DoesNotContain("Expected:", report.Attention[0]);
    }

    [Fact]
    public void SuggestionLine_DoesNotIncreaseAttentionCount()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C without numeric value.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken,
                JsonPayload = "{\"expected\":\"temperature numeric value\",\"examples\":[\"0 \\u00b0C\",\"25 \\u00b0C\",\"-78 \\u00b0C\"],\"token\":\"\\u00b0C\"}"
            }
        ];

        var report = ReportBuilder.Build(0.10, claims, findings);

        // Rendered attention has 2 lines (finding + suggestion)
        Assert.Equal(2, report.Attention.Count);

        // But summary must say "1 item(s) require attention" (findings only)
        Assert.Contains("1 item(s) require attention", report.Summary);
    }

    [Fact]
    public void SuggestionLine_CapsExamplesTo3()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C without numeric value.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken,
                JsonPayload = "{\"expected\":\"temperature numeric value\",\"examples\":[\"0 \\u00b0C\",\"25 \\u00b0C\",\"-78 \\u00b0C\",\"100 \\u00b0C\",\"200 \\u00b0C\"]}"
            }
        ];

        var report = ReportBuilder.Build(0.10, claims, findings);

        string suggestionLine = report.Attention.First(a => a.Contains("Expected:"));
        Assert.Contains("0 °C", suggestionLine);
        Assert.Contains("25 °C", suggestionLine);
        Assert.Contains("-78 °C", suggestionLine);
        Assert.DoesNotContain("100 °C", suggestionLine);
        Assert.DoesNotContain("200 °C", suggestionLine);
    }

    [Fact]
    public void SuggestionLine_ExpectedOnly_OmitsExamplesPortion()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C without numeric value.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken,
                JsonPayload = "{\"expected\":\"temperature numeric value\"}"
            }
        ];

        var report = ReportBuilder.Build(0.10, claims, findings);

        string suggestionLine = report.Attention.First(a => a.Contains("Expected:"));
        Assert.Contains("temperature numeric value", suggestionLine);
        Assert.DoesNotContain("e.g.,", suggestionLine);
    }

    [Fact]
    public void EvidenceLine_DoesNotIncreaseAttentionCount()
    {
        List<ExtractedClaim> claims = [];
        List<ValidationFinding> findings =
        [
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "MalformedChemicalTokenValidator",
                Status = ValidationStatus.Fail,
                Message = "[TEXT.MALFORMED_CHEMICAL_TOKEN] Standalone °C without numeric value.",
                Confidence = 0.8,
                Kind = FindingKind.MalformedChemicalToken,
                EvidenceSnippet = "...added at °C for 2 h...",
                EvidenceStartOffset = 42,
                EvidenceEndOffset = 44
            }
        ];

        var report = ReportBuilder.Build(0.10, claims, findings);

        Assert.Contains(report.Attention, a => a.Contains("\U0001f50e Evidence:"));
        string evidenceLine = report.Attention.First(a => a.Contains("\U0001f50e Evidence:"));
        Assert.StartsWith("   ", evidenceLine);
        Assert.Contains("pos 42-44", evidenceLine);

        int countedAttention = report.Attention.Count(a => !a.StartsWith("   "));
        Assert.Contains($"{countedAttention} item", report.Summary);
    }
}
