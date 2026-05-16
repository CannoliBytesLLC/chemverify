using System.Text.Json;
using ChemVerify.Core.Evaluation;

namespace ChemVerify.Tests;

public class PrecisionAuditorTests
{
    private static PrecisionAuditor.FailureRecord MakeRecord(
        int idx,
        string sourceFile,
        string fullText,
        params (string Validator, string Kind, string Evidence, double Conf)[] findings)
    {
        return new PrecisionAuditor.FailureRecord
        {
            Index = idx,
            SourceFile = sourceFile,
            TextLength = fullText.Length,
            FullText = fullText,
            ClaimCount = 1,
            FindingCount = findings.Length,
            FailCount = findings.Length,
            Findings = findings.Select(f => new PrecisionAuditor.FindingRecord
            {
                Validator = f.Validator,
                Status = "Fail",
                Kind = f.Kind,
                Confidence = f.Conf,
                EvidenceSnippet = f.Evidence,
                Message = $"{f.Validator}: {f.Kind}"
            }).ToList()
        };
    }

    [Fact]
    public void Audit_ProducesSchemaWithAllRequiredFields()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json",
                "stir 2 h then heat 20 h",
                ("NumericContradictionValidator", "Contradiction", "2 h ... 20 h", 0.7))
        };

        PrecisionAuditor.AuditReport report = PrecisionAuditor.Audit(failures,
            new PrecisionAuditor.AuditOptions { TopValidatorCount = 1, SamplesPerValidator = 5 });

        Assert.Equal(1, report.TotalFailureRecords);
        Assert.Equal(1, report.TotalFailFindings);
        Assert.Single(report.SampledFindings);

        PrecisionAuditor.AuditedFinding f = report.SampledFindings[0];
        Assert.Equal("a.json", f.SourceFile);
        Assert.Equal(1, f.ParagraphIndex);
        Assert.Equal("NumericContradictionValidator", f.Validator);
        Assert.Equal("Fail", f.Status);
        Assert.Equal("Contradiction", f.Kind);
        Assert.Equal(0.7, f.Confidence, 3);
        Assert.False(string.IsNullOrEmpty(f.EvidenceSnippet));
        Assert.False(string.IsNullOrEmpty(f.FullText));
        Assert.False(string.IsNullOrEmpty(f.SuspectedFailureClass));
        Assert.Contains("TruePositive", f.SuggestedReviewLabels);
        Assert.Contains("FalsePositive", f.SuggestedReviewLabels);
        Assert.Contains("Ambiguous", f.SuggestedReviewLabels);
    }

    [Fact]
    public void Audit_DeterministicSampling_WithSameSeed()
    {
        var failures = Enumerable.Range(0, 100)
            .Select(i => MakeRecord(i, $"src{i}.json",
                "stir then heat",
                ("NumericContradictionValidator", "Contradiction", $"{i} h ... 20 h", 0.5)))
            .ToList();

        var opts = new PrecisionAuditor.AuditOptions
        {
            TopValidatorCount = 1,
            SamplesPerValidator = 10,
            Seed = 42
        };

        var a = PrecisionAuditor.Audit(failures, opts);
        var b = PrecisionAuditor.Audit(failures, opts);

        Assert.Equal(
            a.SampledFindings.Select(s => s.ParagraphIndex),
            b.SampledFindings.Select(s => s.ParagraphIndex));
    }

    [Fact]
    public void Audit_PreClassifies_SequentialNumericContradiction()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json",
                "stir 2 h then heat 20 h, after which the mixture was cooled",
                ("NumericContradictionValidator", "Contradiction", "2 h then 20 h", 0.6))
        };

        var report = PrecisionAuditor.Audit(failures);
        Assert.Equal("SequentialOperationIssue", report.SampledFindings[0].SuspectedFailureClass);
    }

    [Fact]
    public void Audit_PreClassifies_ProductPropertySolventTemp()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json",
                "bp 119 °C at 1 mmHg",
                ("SolventTemperatureValidator", "ImplausibleSolventTemperature", "bp 119 °C at 1 mmHg", 0.8))
        };

        var report = PrecisionAuditor.Audit(failures);
        Assert.Equal("ProductPropertyIssue", report.SampledFindings[0].SuspectedFailureClass);
    }

    [Fact]
    public void Audit_PreClassifies_AqueousMediumMissingSolvent()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json",
                "the reaction was performed in 6 M HCl at room temperature",
                ("MissingSolventValidator", "MissingSolvent", "in 6 M HCl", 0.5))
        };

        var report = PrecisionAuditor.Audit(failures);
        Assert.Equal("MediumRoleIssue", report.SampledFindings[0].SuspectedFailureClass);
    }

    [Fact]
    public void Audit_PreClassifies_ReagentLifecycleAfterWorkup()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json",
                "NaH was added to THF; after 2 h the mixture was quenched and washed with water and brine",
                ("IncompatibleReagentSolventValidator", "IncompatibleReagentSolvent", "water and brine", 0.5))
        };

        var report = PrecisionAuditor.Audit(failures);
        Assert.Equal("ReagentLifecycleIssue", report.SampledFindings[0].SuspectedFailureClass);
    }

    [Fact]
    public void Audit_PreClassifies_LimitingReagentForYieldMass()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json",
                "Pd catalyst loading 5 mg afforded 250 mg of product",
                ("YieldMassConsistencyValidator", "YieldMassMismatch", "5 mg", 0.6))
        };

        var report = PrecisionAuditor.Audit(failures);
        Assert.Equal("LimitingReagentIssue", report.SampledFindings[0].SuspectedFailureClass);
    }

    [Fact]
    public void Audit_CsvExport_HasHeaderAndRowPerSample()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json", "stir 2 h then heat 20 h",
                ("NumericContradictionValidator", "Contradiction", "2 h then 20 h", 0.6)),
            MakeRecord(2, "b.json", "bp 119 °C at 1 mmHg",
                ("SolventTemperatureValidator", "ImplausibleSolventTemperature", "bp 119 °C at 1 mmHg", 0.8))
        };

        var report = PrecisionAuditor.Audit(failures,
            new PrecisionAuditor.AuditOptions { TopValidatorCount = 5, SamplesPerValidator = 5 });

        string tmp = Path.Combine(Path.GetTempPath(), $"audit_{Guid.NewGuid():N}.csv");
        try
        {
            PrecisionAuditor.WriteCsv(report, tmp);
            string[] lines = File.ReadAllLines(tmp);
            Assert.StartsWith("SourceFile,ParagraphIndex,Validator,", lines[0]);
            Assert.Equal(report.SampledFindings.Count + 1, lines.Length);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Audit_SummaryCounts_MatchSampledFindings()
    {
        var failures = new[]
        {
            MakeRecord(1, "a.json", "stir 2 h then heat 20 h",
                ("NumericContradictionValidator", "Contradiction", "2 h then 20 h", 0.5)),
            MakeRecord(2, "a.json", "stir 5 h then heat 25 h",
                ("NumericContradictionValidator", "Contradiction", "5 h then 25 h", 0.5)),
            MakeRecord(3, "b.json", "bp 119 °C at 1 mmHg",
                ("SolventTemperatureValidator", "ImplausibleSolventTemperature", "bp 119 °C at 1 mmHg", 0.5))
        };

        var report = PrecisionAuditor.Audit(failures,
            new PrecisionAuditor.AuditOptions { TopValidatorCount = 5, SamplesPerValidator = 50 });

        var numeric = report.ValidatorSummaries.Single(v => v.Validator == "NumericContradictionValidator");
        Assert.Equal(2, numeric.FailCount);
        Assert.Equal(2, numeric.SampledCount);

        int totalSampled = report.ValidatorSummaries.Sum(v => v.SampledCount);
        Assert.Equal(totalSampled, report.SampledFindings.Count);

        int totalByClass = report.ClassSummaries.Sum(c => c.Count);
        Assert.Equal(totalSampled, totalByClass);
    }

    [Fact]
    public void Audit_LoadFailures_ReadsHarnessSchema()
    {
        var failures = new[]
        {
            MakeRecord(7, "x.json", "stir 2 h then heat 20 h",
                ("NumericContradictionValidator", "Contradiction", "2 h then 20 h", 0.6))
        };

        string tmp = Path.Combine(Path.GetTempPath(), $"failures_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(failures));
            var loaded = PrecisionAuditor.LoadFailures(tmp);
            Assert.Single(loaded);
            Assert.Equal(7, loaded[0].Index);
            Assert.Equal("NumericContradictionValidator", loaded[0].Findings[0].Validator);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
