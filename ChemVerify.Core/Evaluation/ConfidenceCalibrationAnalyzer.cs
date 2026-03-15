using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Evaluation;

namespace ChemVerify.Core.Evaluation;

/// <summary>
/// Analyzes whether validator confidence values are meaningful.
/// Computes average confidence by outcome category (TP/FP/TN/FN),
/// histogram buckets, and a suggested threshold table.
/// </summary>
/// <remarks>
/// A well-calibrated validator should show higher average confidence for
/// true positives than false positives. The threshold table helps identify
/// cutoff points that maximize precision at acceptable recall levels.
/// </remarks>
public sealed class ConfidenceCalibrationAnalyzer
{
    /// <summary>Bucket boundaries for the confidence histogram (0.0, 0.1, 0.2, … 1.0).</summary>
    private static readonly double[] BucketEdges = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.01];

    /// <summary>Candidate thresholds for the suggestion table.</summary>
    private static readonly double[] CandidateThresholds = [0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];

    /// <summary>
    /// Analyzes a set of classified outcomes for a single validator.
    /// </summary>
    public ConfidenceCalibration Analyze(IReadOnlyList<ClassifiedOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        // Categorize outcomes
        var tp = outcomes.Where(o =>
            o.ExpectedStatus == ValidationStatus.Fail && o.ActualStatus == ValidationStatus.Fail).ToList();
        var fp = outcomes.Where(o =>
            o.ExpectedStatus == ValidationStatus.Pass && o.ActualStatus == ValidationStatus.Fail).ToList();
        var tn = outcomes.Where(o =>
            o.ExpectedStatus == ValidationStatus.Pass && o.ActualStatus != ValidationStatus.Fail).ToList();
        var fn = outcomes.Where(o =>
            o.ExpectedStatus == ValidationStatus.Fail && o.ActualStatus != ValidationStatus.Fail
            && o.ActualStatus != ValidationStatus.Unverified).ToList();

        double? avgTp = tp.Count > 0 ? tp.Average(o => o.Confidence) : null;
        double? avgFp = fp.Count > 0 ? fp.Average(o => o.Confidence) : null;
        double? avgTn = tn.Count > 0 ? tn.Average(o => o.Confidence) : null;
        double? avgFn = fn.Count > 0 ? fn.Average(o => o.Confidence) : null;

        // Build confidence histogram
        var allDecided = outcomes
            .Where(o => o.ActualStatus != ValidationStatus.Unverified
                        && o.ExpectedStatus != ValidationStatus.Unverified)
            .ToList();

        List<ConfidenceBucket> buckets = [];
        for (int i = 0; i < BucketEdges.Length - 1; i++)
        {
            double lo = BucketEdges[i];
            double hi = BucketEdges[i + 1];
            var inBucket = allDecided.Where(o => o.Confidence >= lo && o.Confidence < hi).ToList();
            int correct = inBucket.Count(IsCorrect);
            int incorrect = inBucket.Count - correct;
            buckets.Add(new ConfidenceBucket(lo, hi, inBucket.Count, correct, incorrect));
        }

        // Threshold suggestion table
        // For each threshold: if we only trust findings with confidence >= threshold, what's precision/recall?
        int totalExpectedFails = outcomes.Count(o => o.ExpectedStatus == ValidationStatus.Fail);
        List<ThresholdRow> thresholds = [];
        foreach (double threshold in CandidateThresholds)
        {
            // Fail findings at or above threshold
            var aboveThreshold = outcomes
                .Where(o => o.ActualStatus == ValidationStatus.Fail && o.Confidence >= threshold)
                .ToList();
            int tpAbove = aboveThreshold.Count(o => o.ExpectedStatus == ValidationStatus.Fail);
            int fpAbove = aboveThreshold.Count(o => o.ExpectedStatus == ValidationStatus.Pass);

            double? precision = (tpAbove + fpAbove) > 0
                ? (double)tpAbove / (tpAbove + fpAbove)
                : null;
            double? recall = totalExpectedFails > 0
                ? (double)tpAbove / totalExpectedFails
                : null;

            thresholds.Add(new ThresholdRow(threshold, precision, recall, aboveThreshold.Count));
        }

        return new ConfidenceCalibration(avgTp, avgFp, avgTn, avgFn, buckets, thresholds);
    }

    private static bool IsCorrect(ClassifiedOutcome o)
    {
        return (o.ExpectedStatus == ValidationStatus.Fail && o.ActualStatus == ValidationStatus.Fail)
            || (o.ExpectedStatus == ValidationStatus.Pass && o.ActualStatus != ValidationStatus.Fail);
    }
}
