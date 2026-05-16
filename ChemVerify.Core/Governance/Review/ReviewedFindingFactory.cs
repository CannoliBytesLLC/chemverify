using ChemVerify.Abstractions.Governance;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Governance.Review;

/// <summary>
/// Minimal in-memory builder that materializes <see cref="ReviewedFinding"/>
/// records from the active <see cref="ValidationFinding"/> set so reviewers
/// can begin triaging immediately. Persistence is deferred to a later phase.
/// </summary>
public sealed class ReviewedFindingFactory
{
    public IReadOnlyList<ReviewedFinding> CreateFromFindings(
        Guid runId,
        IReadOnlyList<ValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        List<ReviewedFinding> reviewed = new(findings.Count);
        foreach (ValidationFinding f in findings)
        {
            if (f.IsDiagnostic || f.IsSuppressed)
            {
                continue;
            }

            reviewed.Add(new ReviewedFinding
            {
                FindingId = f.Id,
                RunId = runId,
                ValidatorName = f.ValidatorName,
                Kind = f.Kind,
                Disposition = ReviewDisposition.PendingReview
            });
        }

        return reviewed;
    }

    /// <summary>
    /// Aggregates reviewer dispositions into per-validator precision metrics.
    /// </summary>
    public IReadOnlyList<ValidatorPrecisionStats> ComputeStats(IEnumerable<ReviewedFinding> reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);

        return reviewed
            .GroupBy(r => r.ValidatorName, StringComparer.Ordinal)
            .Select(g => new ValidatorPrecisionStats(
                ValidatorName: g.Key,
                TotalReviewed: g.Count(),
                Confirmed: g.Count(r => r.Disposition == ReviewDisposition.ConfirmedIssue),
                FalsePositive: g.Count(r => r.Disposition == ReviewDisposition.FalsePositive),
                BenignVariation: g.Count(r => r.Disposition == ReviewDisposition.BenignVariation),
                Pending: g.Count(r => r.Disposition == ReviewDisposition.PendingReview)))
            .OrderBy(s => s.ValidatorName, StringComparer.Ordinal)
            .ToArray();
    }
}
