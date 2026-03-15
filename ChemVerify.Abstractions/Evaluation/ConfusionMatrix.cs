namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// Binary confusion matrix for a single validator evaluated against the gold set.
/// "Positive" = the validator emitted Fail; "Negative" = the validator emitted Pass or no finding.
/// </summary>
/// <param name="TruePositives">Validator said Fail AND gold-set agrees (correctly caught an issue).</param>
/// <param name="FalsePositives">Validator said Fail BUT gold-set says Pass (false alarm).</param>
/// <param name="TrueNegatives">Validator said Pass/absent AND gold-set agrees (correctly clean).</param>
/// <param name="FalseNegatives">Validator said Pass/absent BUT gold-set says Fail (missed issue).</param>
/// <param name="UnverifiedCount">
/// Gold-set items where the validator produced <c>Unverified</c> — excluded from TP/FP/TN/FN
/// but tracked separately because high unverified counts indicate extraction gaps.
/// </param>
public sealed record ConfusionMatrix(
    int TruePositives,
    int FalsePositives,
    int TrueNegatives,
    int FalseNegatives,
    int UnverifiedCount)
{
    /// <summary>Total evaluated (excludes Unverified).</summary>
    public int Total => TruePositives + FalsePositives + TrueNegatives + FalseNegatives;

    /// <summary>TP / (TP + FP). Returns <c>null</c> when denominator is zero.</summary>
    public double? Precision => TruePositives + FalsePositives > 0
        ? (double)TruePositives / (TruePositives + FalsePositives)
        : null;

    /// <summary>TP / (TP + FN). Returns <c>null</c> when denominator is zero.</summary>
    public double? Recall => TruePositives + FalseNegatives > 0
        ? (double)TruePositives / (TruePositives + FalseNegatives)
        : null;

    /// <summary>FP / (FP + TN). Returns <c>null</c> when denominator is zero.</summary>
    public double? FalsePositiveRate => FalsePositives + TrueNegatives > 0
        ? (double)FalsePositives / (FalsePositives + TrueNegatives)
        : null;

    /// <summary>FN / (FN + TP). Returns <c>null</c> when denominator is zero.</summary>
    public double? FalseNegativeRate => FalseNegatives + TruePositives > 0
        ? (double)FalseNegatives / (FalseNegatives + TruePositives)
        : null;

    /// <summary>Harmonic mean of precision and recall.</summary>
    public double? F1Score => Precision is > 0 && Recall is > 0
        ? 2.0 * Precision.Value * Recall.Value / (Precision.Value + Recall.Value)
        : null;
}
