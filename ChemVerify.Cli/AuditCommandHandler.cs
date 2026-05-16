using ChemVerify.Core.Evaluation;

namespace ChemVerify.Cli;

/// <summary>
/// Handles the <c>audit</c> CLI command: loads a Pistachio failure JSON file,
/// runs the <see cref="PrecisionAuditor"/>, and writes JSON/CSV plus a short
/// console summary.
/// </summary>
public static class AuditCommandHandler
{
    public static int Execute(
        string inputPath,
        string? outDir,
        int topValidators,
        int samplesPerValidator,
        int seed,
        TextWriter stdout,
        TextWriter stderr)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                stderr.WriteLine($"Error: input file not found — {inputPath}");
                return ExitCodes.EngineError;
            }

            string targetDir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
            Directory.CreateDirectory(targetDir);

            IReadOnlyList<PrecisionAuditor.FailureRecord> failures =
                PrecisionAuditor.LoadFailures(inputPath);

            PrecisionAuditor.AuditReport report = PrecisionAuditor.Audit(failures, new PrecisionAuditor.AuditOptions
            {
                TopValidatorCount = topValidators,
                SamplesPerValidator = samplesPerValidator,
                Seed = seed
            });

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string jsonPath = Path.Combine(targetDir, $"precision_audit_{stamp}.json");
            string csvPath = Path.Combine(targetDir, $"precision_audit_{stamp}.csv");

            PrecisionAuditor.WriteJson(report, jsonPath);
            PrecisionAuditor.WriteCsv(report, csvPath);

            WriteSummary(report, jsonPath, csvPath, stdout);
            return ExitCodes.Ok;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Audit error: {ex.Message}");
            return ExitCodes.EngineError;
        }
    }

    private static void WriteSummary(
        PrecisionAuditor.AuditReport report,
        string jsonPath,
        string csvPath,
        TextWriter stdout)
    {
        stdout.WriteLine("==============================================================");
        stdout.WriteLine("  Precision Audit Summary");
        stdout.WriteLine("--------------------------------------------------------------");
        stdout.WriteLine($"  Failure records      : {report.TotalFailureRecords:N0}");
        stdout.WriteLine($"  Total fail findings  : {report.TotalFailFindings:N0}");
        stdout.WriteLine($"  Seed                 : {report.Seed}");
        stdout.WriteLine($"  Samples / validator  : {report.SamplesPerValidator}");
        stdout.WriteLine("--------------------------------------------------------------");
        stdout.WriteLine("  Top validators by fail count");
        foreach (PrecisionAuditor.ValidatorSummary v in report.ValidatorSummaries)
        {
            stdout.WriteLine($"    {v.Validator,-45} fails={v.FailCount,7:N0}  sampled={v.SampledCount}");
        }
        stdout.WriteLine("--------------------------------------------------------------");
        stdout.WriteLine("  Suspected failure-class distribution (sampled)");
        foreach (PrecisionAuditor.ClassSummary c in report.ClassSummaries)
        {
            stdout.WriteLine($"    {c.SuspectedFailureClass,-30} {c.Count,5}");
        }
        stdout.WriteLine("--------------------------------------------------------------");
        stdout.WriteLine("  Recommended next tuning targets");
        foreach (PrecisionAuditor.TuningRecommendation r in report.Recommendations)
        {
            stdout.WriteLine($"    [{r.EstimatedImpact,6:N0}] {r.Validator}");
            stdout.WriteLine($"             {r.Recommendation}");
            stdout.WriteLine($"             {r.Rationale}");
        }
        stdout.WriteLine("--------------------------------------------------------------");
        stdout.WriteLine($"  Audit JSON : {jsonPath}");
        stdout.WriteLine($"  Audit CSV  : {csvPath}");
        stdout.WriteLine("==============================================================");
    }
}
