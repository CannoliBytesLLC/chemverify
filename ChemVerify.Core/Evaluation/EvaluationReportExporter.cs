using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChemVerify.Abstractions.Evaluation;

namespace ChemVerify.Core.Evaluation;

/// <summary>
/// Exports an <see cref="EvaluationSummary"/> to JSON, CSV, and Markdown formats.
/// All methods are pure (no I/O) — they return strings that the caller can write to disk.
/// </summary>
public sealed class EvaluationReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes the full evaluation summary to indented JSON.
    /// </summary>
    public string ExportJson(EvaluationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    /// <summary>
    /// Exports the validator leaderboard and per-validator confusion matrices as CSV.
    /// Returns a dictionary of filename → CSV content.
    /// </summary>
    public Dictionary<string, string> ExportCsv(EvaluationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Dictionary<string, string> files = [];

        // Leaderboard CSV
        files["leaderboard.csv"] = BuildLeaderboardCsv(summary.Leaderboard);

        // Per-validator metrics CSV
        files["validator_metrics.csv"] = BuildValidatorMetricsCsv(summary.ValidatorResults);

        // Confidence calibration CSV
        files["confidence_calibration.csv"] = BuildCalibrationCsv(summary.ValidatorResults);

        // Stratified metrics CSV
        files["stratified_metrics.csv"] = BuildStratifiedCsv(summary.StratifiedResults);

        // Review queue CSV
        files["review_queue.csv"] = BuildReviewQueueCsv(summary.ReviewQueue);

        return files;
    }

    /// <summary>
    /// Exports a human-readable Markdown report with leaderboard, per-validator details,
    /// confidence calibration summary, and stratified analysis.
    /// </summary>
    public string ExportMarkdown(EvaluationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        StringBuilder sb = new();

        sb.AppendLine("# ChemVerify Validator Evaluation Report");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Run:** {summary.RunTimestamp:O}  ");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Gold-set size:** {summary.GoldSetCount}  ");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Validators evaluated:** {summary.ValidatorResults.Count}");
        sb.AppendLine();

        // Leaderboard
        sb.AppendLine("## Validator Leaderboard");
        sb.AppendLine();
        sb.AppendLine("| Rank | Validator | Precision | Recall | F1 | Evaluated | FP | Unverified |");
        sb.AppendLine("|------|-----------|-----------|--------|----|-----------|----|------------|");
        foreach (LeaderboardEntry e in summary.Leaderboard)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {e.Rank} | {e.ValidatorName} | {Pct(e.Precision)} | {Pct(e.Recall)} | {Pct(e.F1Score)} | {e.TotalEvaluated} | {e.FalsePositiveCount} | {e.UnverifiedCount} |");
        }
        sb.AppendLine();

        // Per-validator details
        sb.AppendLine("## Per-Validator Details");
        sb.AppendLine();
        foreach (ValidatorAuditResult v in summary.ValidatorResults)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"### {v.ValidatorName}");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **TP:** {v.Matrix.TruePositives}  **FP:** {v.Matrix.FalsePositives}  **TN:** {v.Matrix.TrueNegatives}  **FN:** {v.Matrix.FalseNegatives}  **Unverified:** {v.Matrix.UnverifiedCount}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **Precision:** {Pct(v.Matrix.Precision)}  **Recall:** {Pct(v.Matrix.Recall)}  **F1:** {Pct(v.Matrix.F1Score)}");
            sb.AppendLine();

            // Confidence summary
            sb.AppendLine(CultureInfo.InvariantCulture, $"- Avg confidence — TP: {Num(v.Calibration.AvgConfidenceTruePositives)}, FP: {Num(v.Calibration.AvgConfidenceFalsePositives)}, TN: {Num(v.Calibration.AvgConfidenceTrueNegatives)}, FN: {Num(v.Calibration.AvgConfidenceFalseNegatives)}");
            sb.AppendLine();

            // Threshold table
            if (v.Calibration.SuggestedThresholds.Count > 0)
            {
                sb.AppendLine("| Threshold | Precision | Recall | Count≥ |");
                sb.AppendLine("|-----------|-----------|--------|--------|");
                foreach (ThresholdRow t in v.Calibration.SuggestedThresholds)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"| {t.Threshold:F1} | {Pct(t.PrecisionAtThreshold)} | {Pct(t.RecallAtThreshold)} | {t.CountAboveThreshold} |");
                }
                sb.AppendLine();
            }

            // Top FP/FN (abbreviated)
            if (v.TopFalsePositives.Count > 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"**Top false positives ({v.TopFalsePositives.Count}):** ");
                foreach (MisclassifiedExample ex in v.TopFalsePositives.Take(5))
                    sb.AppendLine(CultureInfo.InvariantCulture, $"- `{ex.GoldSetItemId}` (conf={ex.Confidence:F2}): {Truncate(ex.FindingMessage, 80)}");
                sb.AppendLine();
            }

            if (v.TopFalseNegatives.Count > 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"**Top false negatives ({v.TopFalseNegatives.Count}):** ");
                foreach (MisclassifiedExample ex in v.TopFalseNegatives.Take(5))
                    sb.AppendLine(CultureInfo.InvariantCulture, $"- `{ex.GoldSetItemId}` (expected={ex.ExpectedStatus}): {Truncate(ex.ParagraphSnippet, 80)}");
                sb.AppendLine();
            }
        }

        // Stratified analysis
        if (summary.StratifiedResults.Count > 0)
        {
            sb.AppendLine("## Stratified Analysis");
            sb.AppendLine();
            sb.AppendLine("| Dimension | Value | Samples | TP | FP | TN | FN | Precision | Recall |");
            sb.AppendLine("|-----------|-------|---------|----|----|----|----|-----------|--------|");
            foreach (StratifiedMetrics s in summary.StratifiedResults)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {s.Key.Dimension} | {s.Key.Value} | {s.SampleCount} | {s.Matrix.TruePositives} | {s.Matrix.FalsePositives} | {s.Matrix.TrueNegatives} | {s.Matrix.FalseNegatives} | {Pct(s.Matrix.Precision)} | {Pct(s.Matrix.Recall)} |");
            }
            sb.AppendLine();
        }

        // Review queue summary
        sb.AppendLine("## Review Queue");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Total items queued:** {summary.ReviewQueue.Count}");
        sb.AppendLine();
        if (summary.ReviewQueue.Count > 0)
        {
            sb.AppendLine("| Priority | Source ID | Reason | Max Conf | Validators |");
            sb.AppendLine("|----------|----------|--------|----------|------------|");
            foreach (ReviewQueueItem q in summary.ReviewQueue.Take(20))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {q.Priority} | {q.SourceId} | {Truncate(q.PriorityReason, 50)} | {q.MaxConfidence:F2} | {string.Join(", ", q.ValidatorNames)} |");
            }
        }

        return sb.ToString();
    }

    // ── CSV builders ────────────────────────────────────────────────────────

    private static string BuildLeaderboardCsv(IReadOnlyList<LeaderboardEntry> leaderboard)
    {
        StringBuilder sb = new();
        sb.AppendLine("Rank,Validator,Precision,Recall,F1,TotalEvaluated,FalsePositives,Unverified");
        foreach (LeaderboardEntry e in leaderboard)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{e.Rank},{Csv(e.ValidatorName)},{CsvNum(e.Precision)},{CsvNum(e.Recall)},{CsvNum(e.F1Score)},{e.TotalEvaluated},{e.FalsePositiveCount},{e.UnverifiedCount}");
        }
        return sb.ToString();
    }

    private static string BuildValidatorMetricsCsv(IReadOnlyList<ValidatorAuditResult> results)
    {
        StringBuilder sb = new();
        sb.AppendLine("Validator,TP,FP,TN,FN,Unverified,Precision,Recall,FPR,FNR,F1");
        foreach (ValidatorAuditResult v in results)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{Csv(v.ValidatorName)},{v.Matrix.TruePositives},{v.Matrix.FalsePositives},{v.Matrix.TrueNegatives},{v.Matrix.FalseNegatives},{v.Matrix.UnverifiedCount},{CsvNum(v.Matrix.Precision)},{CsvNum(v.Matrix.Recall)},{CsvNum(v.Matrix.FalsePositiveRate)},{CsvNum(v.Matrix.FalseNegativeRate)},{CsvNum(v.Matrix.F1Score)}");
        }
        return sb.ToString();
    }

    private static string BuildCalibrationCsv(IReadOnlyList<ValidatorAuditResult> results)
    {
        StringBuilder sb = new();
        sb.AppendLine("Validator,AvgConfTP,AvgConfFP,AvgConfTN,AvgConfFN,Bucket,Count,Correct,Incorrect,Accuracy");
        foreach (ValidatorAuditResult v in results)
        {
            foreach (ConfidenceBucket b in v.Calibration.Buckets.Where(b => b.Count > 0))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{Csv(v.ValidatorName)},{CsvNum(v.Calibration.AvgConfidenceTruePositives)},{CsvNum(v.Calibration.AvgConfidenceFalsePositives)},{CsvNum(v.Calibration.AvgConfidenceTrueNegatives)},{CsvNum(v.Calibration.AvgConfidenceFalseNegatives)},{b.LowerBound:F1}-{b.UpperBound:F1},{b.Count},{b.CorrectCount},{b.IncorrectCount},{CsvNum(b.Accuracy)}");
            }
        }
        return sb.ToString();
    }

    private static string BuildStratifiedCsv(IReadOnlyList<StratifiedMetrics> stratified)
    {
        StringBuilder sb = new();
        sb.AppendLine("Dimension,Value,Samples,TP,FP,TN,FN,Unverified,Precision,Recall");
        foreach (StratifiedMetrics s in stratified)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{Csv(s.Key.Dimension)},{Csv(s.Key.Value)},{s.SampleCount},{s.Matrix.TruePositives},{s.Matrix.FalsePositives},{s.Matrix.TrueNegatives},{s.Matrix.FalseNegatives},{s.Matrix.UnverifiedCount},{CsvNum(s.Matrix.Precision)},{CsvNum(s.Matrix.Recall)}");
        }
        return sb.ToString();
    }

    private static string BuildReviewQueueCsv(IReadOnlyList<ReviewQueueItem> queue)
    {
        StringBuilder sb = new();
        sb.AppendLine("Priority,SourceId,Reason,MaxConfidence,Validators,HasConflict,UnverifiedCount");
        foreach (ReviewQueueItem q in queue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{q.Priority},{Csv(q.SourceId)},{Csv(q.PriorityReason)},{q.MaxConfidence:F4},{Csv(string.Join("; ", q.ValidatorNames))},{q.HasConflictingSignals},{q.UnverifiedCount}");
        }
        return sb.ToString();
    }

    // ── Formatting helpers ──────────────────────────────────────────────────

    private static string Pct(double? v) => v.HasValue ? $"{v.Value:P1}" : "—";
    private static string Num(double? v) => v.HasValue ? $"{v.Value:F3}" : "—";
    private static string CsvNum(double? v) => v.HasValue ? v.Value.ToString("F4", CultureInfo.InvariantCulture) : "";
    private static string Csv(string? v) => v is null ? "" : $"\"{v.Replace("\"", "\"\"")}\"";

    private static string Truncate(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxLen ? s : string.Concat(s.AsSpan(0, maxLen), "…");
    }
}
