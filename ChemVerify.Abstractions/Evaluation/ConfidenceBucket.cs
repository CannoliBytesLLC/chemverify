namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// A histogram bucket for confidence calibration.
/// Groups findings whose confidence falls within [<see cref="LowerBound"/>, <see cref="UpperBound"/>).
/// </summary>
/// <param name="LowerBound">Inclusive lower bound of this bucket (e.g. 0.6).</param>
/// <param name="UpperBound">Exclusive upper bound of this bucket (e.g. 0.7).</param>
/// <param name="Count">Total findings in this bucket.</param>
/// <param name="CorrectCount">Findings in this bucket where the validator was correct.</param>
/// <param name="IncorrectCount">Findings in this bucket where the validator was wrong.</param>
public sealed record ConfidenceBucket(
    double LowerBound,
    double UpperBound,
    int Count,
    int CorrectCount,
    int IncorrectCount)
{
    /// <summary>Fraction of findings in this bucket that were correct.</summary>
    public double? Accuracy => Count > 0 ? (double)CorrectCount / Count : null;
}
