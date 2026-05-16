using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Governance.Benchmarking;

/// <summary>
/// Aggregate distribution of governance-relevant metrics over a set of
/// findings. Produced by <see cref="BenchmarkSummaryGenerator"/>.
/// </summary>
public sealed record BenchmarkSummary(
    int TotalFindings,
    int ActiveFindings,
    int SuppressedFindings,
    int DowngradedFindings,
    int DiagnosticFindings,
    IReadOnlyDictionary<Severity, int> SeverityHistogram,
    IReadOnlyList<ValidatorPrecisionMetric> ValidatorLeaderboard,
    IReadOnlyList<ValidatorFrequency> NoisiestValidators,
    IReadOnlyList<ValidatorFrequency> MostSuppressedValidators,
    IReadOnlyList<KindFrequency> TopKinds);

public sealed record ValidatorFrequency(string ValidatorName, int Count, int Suppressed);
public sealed record KindFrequency(string Kind, int Count);

/// <summary>
/// Per-validator precision/quality metrics computed from a finding corpus.
/// All rates are in [0,1] and are computed deterministically from finding
/// metadata only — no historical store, no external feedback.
/// </summary>
public sealed record ValidatorPrecisionMetric(
    string ValidatorName,
    int TriggerFrequency,
    IReadOnlyDictionary<Severity, int> SeverityDistribution,
    double SuppressionRate,
    double DowngradeRate,
    double HighSeverityHitRate,
    double FalsePositiveEstimate,
    double PrecisionEstimate);

/// <summary>
/// Deterministic, allocation-modest analyzer that summarises a finding set
/// into governance-oriented metrics: severity histogram, top noisy validators,
/// per-validator precision leaderboard, top finding kinds. Designed to run
/// inline during batch executions.
/// </summary>
public sealed class BenchmarkSummaryGenerator
{
    public BenchmarkSummary Generate(IReadOnlyList<ValidationFinding> findings, int top = 10)
    {
        ArgumentNullException.ThrowIfNull(findings);

        Dictionary<Severity, int> sevHist = new();
        foreach (Severity s in Enum.GetValues<Severity>())
        {
            sevHist[s] = 0;
        }

        int active = 0, suppressed = 0, downgraded = 0, diagnostic = 0;

        foreach (ValidationFinding f in findings)
        {
            if (f.IsDiagnostic) { diagnostic++; continue; }
            if (f.IsSuppressed) { suppressed++; continue; }
            if (f.AdjustedConfidence is { } ac && ac < f.Confidence) downgraded++;
            active++;
            if (f.Severity is { } sev)
            {
                sevHist[sev]++;
            }
        }

        IReadOnlyList<ValidatorPrecisionMetric> leaderboard = ComputeLeaderboard(findings);

        var noisiest = findings
            .Where(f => !f.IsDiagnostic)
            .GroupBy(f => f.ValidatorName, StringComparer.Ordinal)
            .Select(g => new ValidatorFrequency(
                g.Key,
                g.Count(),
                g.Count(f => f.IsSuppressed)))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.ValidatorName, StringComparer.Ordinal)
            .Take(top)
            .ToArray();

        var mostSuppressed = noisiest
            .Where(v => v.Suppressed > 0)
            .OrderByDescending(v => v.Suppressed)
            .ThenByDescending(v => v.Count == 0 ? 0.0 : (double)v.Suppressed / v.Count)
            .Take(top)
            .ToArray();

        var kinds = findings
            .Where(f => !f.IsDiagnostic && !f.IsSuppressed && f.Kind is not null)
            .GroupBy(f => f.Kind!, StringComparer.Ordinal)
            .Select(g => new KindFrequency(g.Key, g.Count()))
            .OrderByDescending(k => k.Count)
            .ThenBy(k => k.Kind, StringComparer.Ordinal)
            .Take(top)
            .ToArray();

        return new BenchmarkSummary(
            TotalFindings: findings.Count,
            ActiveFindings: active,
            SuppressedFindings: suppressed,
            DowngradedFindings: downgraded,
            DiagnosticFindings: diagnostic,
            SeverityHistogram: sevHist,
            ValidatorLeaderboard: leaderboard,
            NoisiestValidators: noisiest,
            MostSuppressedValidators: mostSuppressed,
            TopKinds: kinds);
    }

    private static IReadOnlyList<ValidatorPrecisionMetric> ComputeLeaderboard(
        IReadOnlyList<ValidationFinding> findings)
    {
        return findings
            .Where(f => !f.IsDiagnostic && f.ValidatorName is not null)
            .GroupBy(f => f.ValidatorName, StringComparer.Ordinal)
            .Select(g =>
            {
                var members = g.ToArray();
                int total = members.Length;
                int suppressed = members.Count(f => f.IsSuppressed);
                int downgraded = members.Count(f => f.AdjustedConfidence is { } ac && ac < f.Confidence);

                Dictionary<Severity, int> dist = new();
                foreach (Severity s in Enum.GetValues<Severity>()) dist[s] = 0;
                int active = 0, highOrCritical = 0;
                foreach (ValidationFinding f in members)
                {
                    if (f.IsSuppressed) continue;
                    active++;
                    Severity s = f.Severity ?? Severity.Medium;
                    dist[s]++;
                    if (s >= Severity.High) highOrCritical++;
                }

                double suppressionRate = total == 0 ? 0.0 : (double)suppressed / total;
                double downgradeRate   = total == 0 ? 0.0 : (double)downgraded / total;
                double highHit         = active == 0 ? 0.0 : (double)highOrCritical / active;

                // Deterministic estimate: a validator that gets governance to
                // suppress or downgrade most of its outputs is, by definition,
                // producing low-precision signal.
                double fpEstimate = total == 0
                    ? 0.0
                    : Math.Clamp((suppressed + (0.5 * downgraded)) / total, 0.0, 1.0);
                double precisionEstimate = Math.Clamp(1.0 - fpEstimate, 0.0, 1.0);

                return new ValidatorPrecisionMetric(
                    ValidatorName: g.Key,
                    TriggerFrequency: total,
                    SeverityDistribution: dist,
                    SuppressionRate: Math.Round(suppressionRate, 4),
                    DowngradeRate: Math.Round(downgradeRate, 4),
                    HighSeverityHitRate: Math.Round(highHit, 4),
                    FalsePositiveEstimate: Math.Round(fpEstimate, 4),
                    PrecisionEstimate: Math.Round(precisionEstimate, 4));
            })
            .OrderByDescending(m => m.PrecisionEstimate)
            .ThenByDescending(m => m.HighSeverityHitRate)
            .ThenBy(m => m.ValidatorName, StringComparer.Ordinal)
            .ToArray();
    }
}

