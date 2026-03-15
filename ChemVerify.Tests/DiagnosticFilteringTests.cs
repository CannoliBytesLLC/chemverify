using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Tests;

public class DiagnosticFilteringTests
{
    [Fact]
    public void WhenAllFindingsAreDiagnosticThenRiskScoreIsZero()
    {
        var scorer = new RiskScorer();
        var findings = new List<ValidationFinding>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "NumericContradictionValidator",
                Status = ValidationStatus.Unverified,
                Message = "Not comparable",
                Kind = FindingKind.NotComparable,
                Category = FindingCategory.Diagnostic
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "NumericContradictionValidator",
                Status = ValidationStatus.Unverified,
                Message = "Not checkable",
                Kind = FindingKind.NotCheckable,
                Category = FindingCategory.Diagnostic
            }
        };

        double score = scorer.ComputeScore(findings);

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void WhenMixedFindingsThenOnlyActionableAffectRiskScore()
    {
        var scorer = new RiskScorer();
        var findings = new List<ValidationFinding>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "NumericContradictionValidator",
                Status = ValidationStatus.Unverified,
                Message = "Diagnostic: not comparable",
                Kind = FindingKind.NotComparable,
                Category = FindingCategory.Diagnostic
            },
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ValidatorName = "NumericContradictionValidator",
                Status = ValidationStatus.Fail,
                Message = "Contradiction found",
                Kind = FindingKind.Contradiction,
                Category = FindingCategory.Finding
            }
        };

        double score = scorer.ComputeScore(findings);

        Assert.True(score > 0.0, "Score should be non-zero when actionable findings exist");
    }

    [Fact]
    public void WhenReportBuilderExcludesDiagnosticsThenDiagnosticFindingsHidden()
    {
        var claims = new List<ExtractedClaim>();
        var diagnosticFinding = new ValidationFinding
        {
            Id = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            ValidatorName = "NumericContradictionValidator",
            Status = ValidationStatus.Unverified,
            Message = "Not comparable claim",
            Kind = FindingKind.NotComparable,
            Category = FindingCategory.Diagnostic
        };

        var report = ReportBuilder.Build(0.0, claims,
            new List<ValidationFinding> { diagnosticFinding });

        // Diagnostic findings should not appear in any user-facing section
        Assert.Empty(report.Attention);
        Assert.Empty(report.NotVerifiable);
    }

    [Fact]
    public void WhenReportBuilderIncludesDiagnosticsThenDiagnosticFindingsVisible()
    {
        var claims = new List<ExtractedClaim>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "42 mg",
                NormalizedValue = "42",
                Unit = "mg"
            }
        };

        var diagnosticFinding = new ValidationFinding
        {
            Id = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            ClaimId = claims[0].Id,
            ValidatorName = "NumericContradictionValidator",
            Status = ValidationStatus.Unverified,
            Message = "Single numeric claim for this context+unit; cannot check for contradictions.",
            Kind = FindingKind.NotCheckable,
            Category = FindingCategory.Diagnostic
        };

        var report = ReportBuilder.Build(0.0, claims,
            new List<ValidationFinding> { diagnosticFinding },
            includeDiagnostics: true);

        // With includeDiagnostics=true, NotCheckable should appear in NotVerifiable
        Assert.NotEmpty(report.NotVerifiable);
    }
}
