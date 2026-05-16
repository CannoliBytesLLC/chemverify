using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Governance.Clustering;

/// <summary>
/// A semantic group of related <see cref="ValidationFinding"/> instances that
/// likely describe the same underlying issue. Used by governance reports to
/// collapse noise and surface root-cause patterns.
/// </summary>
public sealed record FindingCluster(
    string Id,
    string Theme,
    string Reasoning,
    IReadOnlyList<Guid> FindingIds,
    IReadOnlyList<int> SharedStepIndexes,
    Severity AggregatedSeverity,
    int FindingCount,
    int CriticalCount,
    int HighCount,
    double RiskContribution);

/// <summary>
/// Deterministic clustering of findings into a small set of canonical themes
/// (reaction-condition, workup, yield, atmosphere). The current implementation
/// is intentionally simple — it groups by theme + step index — so it can be
/// extended without changing the public surface.
/// </summary>
public sealed class FindingClusterBuilder
{
    // Per-severity additive contribution to the cluster's risk score.
    private const double CriticalContribution = 0.40;
    private const double HighContribution     = 0.20;
    private const double MediumContribution   = 0.08;
    private const double LowContribution      = 0.02;

    private static readonly IReadOnlyDictionary<string, string> KindToTheme =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FindingKind.SolventTemperatureImplausible] = "ReactionConditions",
            [FindingKind.ImpossibleRefluxCondition] = "ReactionConditions",
            [FindingKind.MissingTemperature] = "ReactionConditions",
            [FindingKind.MissingSolvent] = "ReactionConditions",
            [FindingKind.Contradiction] = "ReactionConditions",
            [FindingKind.PhysicallyImplausibleValue] = "ReactionConditions",

            [FindingKind.AtmosphereContainmentIssue] = "AtmosphereContainment",
            [FindingKind.AmbiguousWorkupTransition] = "AtmosphereContainment",
            [FindingKind.DrynessSemanticContradiction] = "AtmosphereContainment",

            [FindingKind.YieldMassInconsistent] = "YieldPlausibility",
            [FindingKind.MwImplausible] = "YieldPlausibility",
            [FindingKind.MwKnownMismatch] = "YieldPlausibility",
            [FindingKind.EquivInconsistent] = "YieldPlausibility",

            [FindingKind.MissingQuench] = "Workup",
            [FindingKind.ProceduralOrderingAnomaly] = "Workup",

            [FindingKind.IncompatibleReagentSolvent] = "ReagentCompatibility",

            [FindingKind.MalformedChemicalToken] = "TextIntegrity",
            [FindingKind.UnsupportedOrIncompleteClaim] = "TextIntegrity",
            [FindingKind.CitationTraceabilityWeak] = "TextIntegrity",
            [FindingKind.PlaceholderOrMissingToken] = "TextIntegrity"
        };

    public IReadOnlyList<FindingCluster> Build(IReadOnlyList<ValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        if (findings.Count == 0)
        {
            return [];
        }

        List<FindingCluster> clusters = new();

        var grouped = findings
            .Where(f => !f.IsDiagnostic && !f.IsSuppressed && f.Kind is not null)
            .GroupBy(f => KindToTheme.TryGetValue(f.Kind!, out string? t) ? t : "Other", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        int idx = 0;
        foreach (var group in grouped)
        {
            string clusterId = $"cluster-{++idx}";
            var members = group.ToArray();
            var ids = members.Select(f => f.Id).ToArray();
            var steps = members
                .Where(f => f.EvidenceStepIndex.HasValue)
                .Select(f => f.EvidenceStepIndex!.Value)
                .Distinct()
                .OrderBy(i => i)
                .ToArray();

            int crit = 0, high = 0, med = 0, low = 0;
            Severity aggregated = Severity.Info;
            foreach (ValidationFinding f in members)
            {
                f.ClusterId = clusterId;
                Severity s = f.Severity ?? Severity.Medium;
                if ((int)s > (int)aggregated) aggregated = s;
                switch (s)
                {
                    case Severity.Critical: crit++; break;
                    case Severity.High:     high++; break;
                    case Severity.Medium:   med++;  break;
                    case Severity.Low:      low++;  break;
                }
            }

            double risk = (crit * CriticalContribution)
                        + (high * HighContribution)
                        + (med  * MediumContribution)
                        + (low  * LowContribution);
            // Corroboration: clusters with 3+ same-theme findings reinforce each other.
            if (members.Length >= 3)
            {
                risk *= 1.0 + Math.Min(0.5, 0.10 * (members.Length - 2));
            }

            clusters.Add(new FindingCluster(
                Id: clusterId,
                Theme: group.Key,
                Reasoning: BuildReasoning(group.Key, members.Length, crit, high),
                FindingIds: ids,
                SharedStepIndexes: steps,
                AggregatedSeverity: aggregated,
                FindingCount: members.Length,
                CriticalCount: crit,
                HighCount: high,
                RiskContribution: Math.Round(risk, 4)));
        }

        return clusters
            .OrderByDescending(c => c.RiskContribution)
            .ThenByDescending(c => (int)c.AggregatedSeverity)
            .ThenBy(c => c.Theme, StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildReasoning(string theme, int count, int critical, int high)
    {
        if (critical > 0)
        {
            return $"{count} finding(s) in '{theme}' including {critical} critical issue(s).";
        }
        if (high > 0)
        {
            return $"{count} finding(s) in '{theme}' including {high} high-severity issue(s).";
        }
        return $"{count} finding(s) sharing theme '{theme}'.";
    }
}

