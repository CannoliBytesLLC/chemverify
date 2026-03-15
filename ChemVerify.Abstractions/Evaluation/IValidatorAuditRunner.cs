namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// Executes all registered validators against a gold-set dataset and produces
/// a comprehensive <see cref="EvaluationSummary"/> including per-validator metrics,
/// confidence calibration, stratified analysis, and a prioritized review queue.
/// </summary>
public interface IValidatorAuditRunner
{
    /// <summary>
    /// Runs the full evaluation pipeline on the supplied gold-set items.
    /// </summary>
    /// <param name="goldSet">Manually-reviewed benchmark examples.</param>
    /// <returns>A complete evaluation summary with metrics, calibration, and review queue.</returns>
    EvaluationSummary Evaluate(IReadOnlyList<GoldSetItem> goldSet);
}
