using ChemVerify.Abstractions.Models;

namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// A <see cref="ValidationFinding"/> annotated with reviewer disposition,
/// rationale, and timestamps. Designed to feed validator-precision analytics
/// (<see cref="ValidatorPrecisionStats"/>) without altering the underlying
/// finding record.
/// </summary>
public sealed class ReviewedFinding
{
    public required Guid FindingId { get; init; }
    public required Guid RunId { get; init; }
    public required string ValidatorName { get; init; }
    public string? Kind { get; init; }
    public ReviewDisposition Disposition { get; set; } = ReviewDisposition.PendingReview;
    public string? ReviewerId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? ReviewedUtc { get; set; }
}

/// <summary>
/// Aggregate precision metrics for a single validator across a corpus of
/// reviewed findings.
/// </summary>
public sealed record ValidatorPrecisionStats(
    string ValidatorName,
    int TotalReviewed,
    int Confirmed,
    int FalsePositive,
    int BenignVariation,
    int Pending)
{
    public double Precision => TotalReviewed - Pending == 0
        ? 0.0
        : (double)Confirmed / Math.Max(1, TotalReviewed - Pending);
}

/// <summary>
/// Point-in-time snapshot of a validator's performance for drift analysis.
/// </summary>
public sealed record ValidatorPerformanceSnapshot(
    DateTimeOffset CapturedUtc,
    ValidatorPrecisionStats Stats);
