namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// A stratification key that identifies a subgroup for disaggregated metrics.
/// For example: validator=MwConsistencyValidator + domainTag=organometallic.
/// </summary>
/// <param name="Dimension">The stratification dimension (e.g. "ValidatorName", "DomainTag", "ProcedureLengthBucket").</param>
/// <param name="Value">The value within that dimension (e.g. "organometallic", "short").</param>
public sealed record StratificationKey(string Dimension, string Value);

/// <summary>
/// Metrics for a single stratification subgroup.
/// </summary>
/// <param name="Key">Which subgroup this represents.</param>
/// <param name="Matrix">Confusion matrix for this subgroup only.</param>
/// <param name="SampleCount">Number of gold-set items in this subgroup.</param>
public sealed record StratifiedMetrics(
    StratificationKey Key,
    ConfusionMatrix Matrix,
    int SampleCount);
