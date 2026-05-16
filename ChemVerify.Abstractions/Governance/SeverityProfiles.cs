using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// Deterministic mapping from <see cref="FindingKind"/> to a default
/// <see cref="Severity"/>. Used by <c>ISeverityCalculator</c> as the
/// baseline before contextual adjustments.
/// </summary>
/// <remarks>
/// Severity reflects scientific impact, not statistical confidence:
/// physical impossibilities and safety-relevant containment failures are
/// rated higher than formatting or cross-step variation observations.
/// </remarks>
public static class SeverityProfiles
{
    private static readonly IReadOnlyDictionary<string, Severity> Map =
        new Dictionary<string, Severity>(StringComparer.Ordinal)
        {
            // ── Critical: physical impossibility / safety ──────────────
            [FindingKind.PhysicallyImplausibleValue] = Severity.Critical,
            [FindingKind.ImpossibleRefluxCondition]  = Severity.Critical,

            // ── High: scientific validity at risk ──────────────────────
            [FindingKind.IncompatibleReagentSolvent] = Severity.High,
            [FindingKind.AtmosphereContainmentIssue] = Severity.High,
            [FindingKind.SolventTemperatureImplausible] = Severity.High,
            [FindingKind.MissingQuench] = Severity.High,
            [FindingKind.MwImplausible] = Severity.High,
            [FindingKind.YieldMassInconsistent] = Severity.High,
            [FindingKind.MwKnownMismatch] = Severity.High,
            [FindingKind.DrynessSemanticContradiction] = Severity.High,
            [FindingKind.Contradiction] = Severity.High,

            // ── Medium: notable but recoverable ───────────────────────
            [FindingKind.MissingSolvent] = Severity.Medium,
            [FindingKind.MissingTemperature] = Severity.Medium,
            [FindingKind.AmbiguousWorkupTransition] = Severity.Medium,
            [FindingKind.EquivInconsistent] = Severity.Medium,
            [FindingKind.ProceduralOrderingAnomaly] = Severity.Medium,
            [FindingKind.MalformedChemicalToken] = Severity.Medium,
            [FindingKind.UnsupportedOrIncompleteClaim] = Severity.Medium,

            // ── Low: formatting / traceability ────────────────────────
            [FindingKind.PlaceholderOrMissingToken] = Severity.Low,
            [FindingKind.CitationTraceabilityWeak] = Severity.Low,
            [FindingKind.MultiScenario] = Severity.Low,
            [FindingKind.PossiblyTruncated] = Severity.Low,

            // ── Info: explanatory diagnostics ─────────────────────────
            [FindingKind.CrossStepConditionVariation] = Severity.Info,
            [FindingKind.SequentialDuration] = Severity.Info,
            [FindingKind.CheckpointVsTotal] = Severity.Info,
            [FindingKind.DifferentOperation] = Severity.Info,
            [FindingKind.DifferentConditionContext] = Severity.Info,
            [FindingKind.GradientElution] = Severity.Info,
            [FindingKind.WorkupTransitionDetected] = Severity.Info,
            [FindingKind.MwConsistent] = Severity.Info,
            [FindingKind.NotCheckable] = Severity.Info,
            [FindingKind.NotComparable] = Severity.Info,
            [FindingKind.MissingEvidence] = Severity.Info,
            [FindingKind.EntityAmbiguous] = Severity.Info,
            [FindingKind.DifferentPercentKind] = Severity.Info
        };

    /// <summary>
    /// Returns the default severity for the given finding kind, or
    /// <paramref name="fallback"/> when no mapping is defined.
    /// </summary>
    public static Severity GetDefault(string? kind, Severity fallback = Severity.Medium) =>
        kind is not null && Map.TryGetValue(kind, out Severity s) ? s : fallback;
}
