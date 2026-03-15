namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// Full audit metrics for a single validator evaluated against the gold set.
/// Aggregates the confusion matrix, confidence analysis, and top misclassified examples.
/// </summary>
/// <param name="ValidatorName">The validator type name.</param>
/// <param name="Matrix">Confusion matrix (TP/FP/TN/FN/Unverified).</param>
/// <param name="Calibration">Confidence calibration analysis.</param>
/// <param name="TopFalsePositives">
/// Up to 25 false positives, ordered by descending confidence (highest-confidence mistakes first).
/// </param>
/// <param name="TopFalseNegatives">
/// Up to 25 false negatives, ordered by descending gold-set expectation confidence.
/// </param>
/// <param name="TopAmbiguous">
/// Up to 25 examples where the validator produced <c>Unverified</c> but the gold-set
/// expected a definitive verdict — candidates for manual review.
/// </param>
public sealed record ValidatorAuditResult(
    string ValidatorName,
    ConfusionMatrix Matrix,
    ConfidenceCalibration Calibration,
    IReadOnlyList<MisclassifiedExample> TopFalsePositives,
    IReadOnlyList<MisclassifiedExample> TopFalseNegatives,
    IReadOnlyList<MisclassifiedExample> TopAmbiguous);
