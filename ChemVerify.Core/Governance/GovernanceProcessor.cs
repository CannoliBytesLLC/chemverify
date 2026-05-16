using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Governance;
using ChemVerify.Abstractions.Models;
using ChemVerify.Abstractions.Validation;
using ChemVerify.Core.Procedure;

namespace ChemVerify.Core.Governance;

/// <summary>
/// Deterministic governance pass that runs after validators and before risk
/// scoring. Builds the procedure-state replay once per run, then applies
/// suppression, confidence adjustment, and severity calculation to every
/// finding, mutating only the optional governance fields on
/// <see cref="ValidationFinding"/>.
/// </summary>
/// <remarks>
/// All decisions are derived from the inputs — no I/O, no AI inference, no
/// hidden state. Suppressed findings are flagged via
/// <see cref="ValidationFinding.IsSuppressed"/> rather than removed, so audit
/// reports can explain *why* a validator finding was muted.
/// </remarks>
public sealed class GovernanceProcessor
{
    private readonly IReadOnlyList<IFindingSuppressor> _suppressors;
    private readonly IReadOnlyList<IConfidenceAdjuster> _adjusters;
    private readonly ISeverityCalculator _severityCalculator;

    public GovernanceProcessor(
        IEnumerable<IFindingSuppressor> suppressors,
        IEnumerable<IConfidenceAdjuster> adjusters,
        ISeverityCalculator severityCalculator)
    {
        ArgumentNullException.ThrowIfNull(suppressors);
        ArgumentNullException.ThrowIfNull(adjusters);
        ArgumentNullException.ThrowIfNull(severityCalculator);

        _suppressors = suppressors.ToArray();
        _adjusters = adjusters.ToArray();
        _severityCalculator = severityCalculator;
    }

    /// <summary>
    /// Applies suppression / confidence / severity adjustments to every
    /// finding in <paramref name="findings"/>. Returns the same list for
    /// fluent chaining.
    /// </summary>
    public IReadOnlyList<ValidationFinding> Process(
        AiRun run,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyList<ValidationFinding> findings,
        PolicySettings? policy = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(findings);

        if (findings.Count == 0)
        {
            return findings;
        }

        // Build state snapshots once per run; suppressors that need them cast
        // the StateBag to IReadOnlyList<StateSnapshot>.
        IReadOnlyList<StateSnapshot> snapshots = SafeBuildSnapshots(run, claims);

        foreach (ValidationFinding finding in findings)
        {
            if (finding.IsDiagnostic)
            {
                // Diagnostics get severity Info but are never suppressed/scored.
                finding.Severity = Severity.Info;
                continue;
            }

            ValidatorMetadataAttribute? meta = null; // resolved lazily — cheap reflection skipped here
            ValidatorDecisionContext ctx = new()
            {
                Run = run,
                Claims = claims,
                AllFindings = findings,
                Finding = finding,
                ValidatorMetadata = meta,
                Policy = policy,
                StateBag = snapshots
            };

            ApplySuppressors(finding, ctx);
            ApplyConfidenceAdjusters(finding, ctx);
            finding.Severity = _severityCalculator.Calculate(ctx);
        }

        return findings;
    }

    private void ApplySuppressors(ValidationFinding finding, ValidatorDecisionContext ctx)
    {
        foreach (IFindingSuppressor suppressor in _suppressors)
        {
            ValidationOutcomeAdjustment adj = suppressor.Evaluate(ctx);
            if (adj.Reason == SuppressionReason.None)
            {
                continue;
            }

            finding.SuppressionReasonCode = adj.Reason.ToString();
            finding.AdjustmentExplanation = adj.Explanation;

            if (adj.Suppress)
            {
                finding.IsSuppressed = true;
                return;
            }

            if (adj.ConfidenceMultiplier < 1.0)
            {
                double current = finding.AdjustedConfidence ?? finding.Confidence;
                finding.AdjustedConfidence = Math.Clamp(current * adj.ConfidenceMultiplier, 0.0, 1.0);
            }

            return;
        }
    }

    private void ApplyConfidenceAdjusters(ValidationFinding finding, ValidatorDecisionContext ctx)
    {
        if (finding.IsSuppressed)
        {
            return;
        }

        double effective = finding.AdjustedConfidence ?? finding.Confidence;
        bool changed = false;

        foreach (IConfidenceAdjuster adjuster in _adjusters)
        {
            double m = adjuster.GetMultiplier(ctx);
            if (m >= 1.0)
            {
                continue;
            }

            effective *= Math.Clamp(m, 0.0, 1.0);
            changed = true;
        }

        if (changed)
        {
            finding.AdjustedConfidence = Math.Clamp(effective, 0.0, 1.0);
        }
    }

    private static IReadOnlyList<StateSnapshot> SafeBuildSnapshots(AiRun run, IReadOnlyList<ExtractedClaim> claims)
    {
        try
        {
            string text = run.GetAnalyzedText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            return ProcedureStateEngine.Build(text, claims);
        }
        catch
        {
            // State replay is best-effort context for suppressors — never block governance.
            return [];
        }
    }
}
