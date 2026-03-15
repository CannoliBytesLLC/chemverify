using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Evaluation;

namespace ChemVerify.Core.Evaluation;

/// <summary>
/// Builds a prioritized queue of examples for manual inspection.
/// Prioritization rules (highest priority first):
/// <list type="number">
///   <item>High-confidence failures from validators with poor precision (most likely false alarms)</item>
///   <item>Cases where multiple validators fire together (correlated evidence)</item>
///   <item>Cases with conflicting signals (both Pass and Fail findings on the same item)</item>
///   <item>Unverified outcomes (extraction gaps)</item>
///   <item>Remaining high-confidence failures</item>
/// </list>
/// </summary>
public sealed class ReviewQueueBuilder
{
    /// <summary>Precision below this threshold makes a validator "poor".</summary>
    private const double PoorPrecisionThreshold = 0.7;

    /// <summary>Confidence above this threshold makes a finding "high confidence".</summary>
    private const double HighConfidenceThreshold = 0.7;

    /// <summary>Maximum items in the review queue.</summary>
    private const int MaxQueueSize = 200;

    /// <summary>
    /// Builds the review queue from raw evaluation results and per-validator audit metrics.
    /// </summary>
    public IReadOnlyList<ReviewQueueItem> Build(
        IReadOnlyList<GoldSetRunResult> rawResults,
        IReadOnlyList<ValidatorAuditResult> validatorResults)
    {
        ArgumentNullException.ThrowIfNull(rawResults);
        ArgumentNullException.ThrowIfNull(validatorResults);

        // Identify validators with poor precision
        HashSet<string> poorPrecisionValidators = validatorResults
            .Where(v => v.Matrix.Precision is not null && v.Matrix.Precision < PoorPrecisionThreshold)
            .Select(v => v.ValidatorName)
            .ToHashSet(StringComparer.Ordinal);

        List<ScoredQueueCandidate> candidates = [];

        foreach (GoldSetRunResult result in rawResults)
        {
            if (result.Findings.Count == 0)
                continue;

            string[] validatorNames = result.Findings
                .Select(f => f.ValidatorName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            double maxConfidence = result.Findings.Max(f => f.Confidence);

            bool hasConflict = result.Findings.Any(f => f.Status == ValidationStatus.Pass)
                            && result.Findings.Any(f => f.Status == ValidationStatus.Fail);

            int unverifiedCount = result.Findings.Count(f => f.Status == ValidationStatus.Unverified);

            bool multiValidatorFire = validatorNames.Length >= 2
                && result.Findings.Count(f => f.Status == ValidationStatus.Fail) >= 2;

            bool highConfFailFromPoorValidator = result.Findings.Any(f =>
                f.Status == ValidationStatus.Fail
                && f.Confidence >= HighConfidenceThreshold
                && poorPrecisionValidators.Contains(f.ValidatorName));

            // Compute priority score (lower = higher priority)
            int priority = ComputePriority(
                highConfFailFromPoorValidator,
                multiValidatorFire,
                hasConflict,
                unverifiedCount,
                maxConfidence);

            string reason = BuildReason(
                highConfFailFromPoorValidator,
                multiValidatorFire,
                hasConflict,
                unverifiedCount,
                maxConfidence);

            candidates.Add(new ScoredQueueCandidate(
                result, validatorNames, maxConfidence, hasConflict, unverifiedCount, priority, reason));
        }

        return candidates
            .OrderBy(c => c.Priority)
            .ThenByDescending(c => c.MaxConfidence)
            .Take(MaxQueueSize)
            .Select(c => new ReviewQueueItem(
                ParagraphText: c.Result.Item.ParagraphText,
                Findings: c.Result.Findings,
                Priority: c.Priority,
                PriorityReason: c.Reason,
                ValidatorNames: c.ValidatorNames,
                MaxConfidence: c.MaxConfidence,
                HasConflictingSignals: c.HasConflict,
                UnverifiedCount: c.UnverifiedCount,
                SourceId: c.Result.Item.Id))
            .ToList();
    }

    private static int ComputePriority(
        bool highConfFailFromPoorValidator,
        bool multiValidatorFire,
        bool hasConflict,
        int unverifiedCount,
        double maxConfidence)
    {
        // Priority bands: 1 = most urgent, 5 = lowest
        if (highConfFailFromPoorValidator) return 1;
        if (multiValidatorFire) return 2;
        if (hasConflict) return 3;
        if (unverifiedCount > 0) return 4;
        if (maxConfidence >= HighConfidenceThreshold) return 5;
        return 6;
    }

    private static string BuildReason(
        bool highConfFailFromPoorValidator,
        bool multiValidatorFire,
        bool hasConflict,
        int unverifiedCount,
        double maxConfidence)
    {
        List<string> reasons = [];

        if (highConfFailFromPoorValidator)
            reasons.Add("High-confidence failure from low-precision validator");
        if (multiValidatorFire)
            reasons.Add("Multiple validators flagged this item");
        if (hasConflict)
            reasons.Add("Conflicting Pass and Fail signals");
        if (unverifiedCount > 0)
            reasons.Add($"{unverifiedCount} unverified finding(s)");
        if (maxConfidence >= HighConfidenceThreshold && reasons.Count == 0)
            reasons.Add($"High-confidence finding ({maxConfidence:F2})");

        return reasons.Count > 0 ? string.Join("; ", reasons) : "General review";
    }

    private sealed record ScoredQueueCandidate(
        GoldSetRunResult Result,
        string[] ValidatorNames,
        double MaxConfidence,
        bool HasConflict,
        int UnverifiedCount,
        int Priority,
        string Reason);
}
