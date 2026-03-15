namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// Confidence calibration analysis for a single validator.
/// Answers: "when the validator says 0.9 confidence, is it actually right 90% of the time?"
/// </summary>
/// <param name="AvgConfidenceTruePositives">Mean confidence for correctly detected failures.</param>
/// <param name="AvgConfidenceFalsePositives">Mean confidence for false alarms.</param>
/// <param name="AvgConfidenceTrueNegatives">Mean confidence for correctly passed items (if the validator emits Pass findings).</param>
/// <param name="AvgConfidenceFalseNegatives">Mean confidence for missed failures (often 0 since the validator didn't fire).</param>
/// <param name="Buckets">Histogram of confidence ranges with accuracy per bucket.</param>
/// <param name="SuggestedThresholds">
/// Threshold table: for each candidate threshold, the resulting precision/recall trade-off.
/// Ordered by ascending threshold.
/// </param>
public sealed record ConfidenceCalibration(
    double? AvgConfidenceTruePositives,
    double? AvgConfidenceFalsePositives,
    double? AvgConfidenceTrueNegatives,
    double? AvgConfidenceFalseNegatives,
    IReadOnlyList<ConfidenceBucket> Buckets,
    IReadOnlyList<ThresholdRow> SuggestedThresholds);

/// <summary>
/// A single row in the suggested threshold table.
/// Shows the precision/recall trade-off at a given confidence cutoff.
/// </summary>
/// <param name="Threshold">Confidence cutoff (findings below this are treated as Pass).</param>
/// <param name="PrecisionAtThreshold">Precision if we only count findings ≥ threshold as Fail.</param>
/// <param name="RecallAtThreshold">Recall if we only count findings ≥ threshold as Fail.</param>
/// <param name="CountAboveThreshold">Number of Fail findings at or above this threshold.</param>
public sealed record ThresholdRow(
    double Threshold,
    double? PrecisionAtThreshold,
    double? RecallAtThreshold,
    int CountAboveThreshold);
