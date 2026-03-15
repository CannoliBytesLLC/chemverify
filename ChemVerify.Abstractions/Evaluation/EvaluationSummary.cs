namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// Top-level evaluation summary produced by the audit runner.
/// Designed for serialization to JSON, CSV, and Markdown reports.
/// </summary>
/// <param name="RunTimestamp">When the evaluation was executed.</param>
/// <param name="GoldSetCount">Total number of gold-set items evaluated.</param>
/// <param name="ValidatorResults">Per-validator audit results, keyed by validator name.</param>
/// <param name="StratifiedResults">
/// Disaggregated metrics across all stratification dimensions.
/// </param>
/// <param name="ReviewQueue">
/// Prioritized list of items needing manual review.
/// </param>
/// <param name="Leaderboard">
/// Validators ranked by usefulness (precision × recall, descending).
/// </param>
public sealed record EvaluationSummary(
    DateTimeOffset RunTimestamp,
    int GoldSetCount,
    IReadOnlyList<ValidatorAuditResult> ValidatorResults,
    IReadOnlyList<StratifiedMetrics> StratifiedResults,
    IReadOnlyList<ReviewQueueItem> ReviewQueue,
    IReadOnlyList<LeaderboardEntry> Leaderboard);

/// <summary>
/// A single entry in the validator leaderboard, ranking validators by quality.
/// </summary>
/// <param name="Rank">1-based rank (1 = best).</param>
/// <param name="ValidatorName">The validator being ranked.</param>
/// <param name="Precision">Precision from the confusion matrix.</param>
/// <param name="Recall">Recall from the confusion matrix.</param>
/// <param name="F1Score">Harmonic mean of precision and recall.</param>
/// <param name="TotalEvaluated">Number of gold-set items this validator was evaluated against.</param>
/// <param name="FalsePositiveCount">Total false positives.</param>
/// <param name="UnverifiedCount">Total unverified outcomes.</param>
public sealed record LeaderboardEntry(
    int Rank,
    string ValidatorName,
    double? Precision,
    double? Recall,
    double? F1Score,
    int TotalEvaluated,
    int FalsePositiveCount,
    int UnverifiedCount);
