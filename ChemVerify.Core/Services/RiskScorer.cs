using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Services;

public class RiskScorer : IRiskScorer
{
    private const double FailWeight = 1.0;
    private const double UnverifiedWeight = 0.3;
    private const double PassWeight = 0.0;

    private const double ChemHighWeight = 0.35;
    private const double ChemMediumWeight = 0.15;

    private const double TextIntegrityWeight = 0.10;
    private const double DampenedDoiFailWeight = 0.15;

    private static readonly HashSet<string> ChemHighKinds = new(StringComparer.Ordinal)
    {
        FindingKind.IncompatibleReagentSolvent,
        FindingKind.MissingQuench
    };

    private static readonly HashSet<string> ChemMediumKinds = new(StringComparer.Ordinal)
    {
        FindingKind.MissingSolvent,
        FindingKind.MissingTemperature,
        FindingKind.AmbiguousWorkupTransition,
        FindingKind.EquivInconsistent,
        FindingKind.MwImplausible,
        FindingKind.YieldMassInconsistent
    };

    private static readonly HashSet<string> TextIntegrityKinds = new(StringComparer.Ordinal)
    {
        FindingKind.MalformedChemicalToken,
        FindingKind.UnsupportedOrIncompleteClaim,
        FindingKind.CitationTraceabilityWeak,
        FindingKind.PlaceholderOrMissingToken
    };

    public double ComputeScore(IReadOnlyList<ValidationFinding> findings, PolicySettings? policy = null)
    {
        if (findings.Count == 0)
        {
            return 0.0;
        }

        // Exclude diagnostic observations — they are informational and should not affect risk
        // Also exclude governance-suppressed findings — the precision layer ruled them out.
        IReadOnlyList<ValidationFinding> actionable = findings
            .Where(f => !f.IsDiagnostic && !f.IsSuppressed)
            .ToList();

        if (actionable.Count == 0)
        {
            return 0.0;
        }

        bool dampenDoi = policy?.DampenDoiFailSeverity == true;

        // Separate chemistry / text-integrity findings from general findings
        List<ValidationFinding> general = new();
        double chemAdditiveScore = 0.0;
        double textIntegrityAdditiveScore = 0.0;

        foreach (ValidationFinding f in actionable)
        {
            if (f.Kind is not null && ChemHighKinds.Contains(f.Kind))
            {
                chemAdditiveScore += ChemHighWeight;
            }
            else if (f.Kind is not null && ChemMediumKinds.Contains(f.Kind))
            {
                chemAdditiveScore += ChemMediumWeight;
            }
            else if (f.Kind is not null && TextIntegrityKinds.Contains(f.Kind))
            {
                textIntegrityAdditiveScore += TextIntegrityWeight;
            }
            else
            {
                general.Add(f);
            }
        }

        double baseScore = 0.0;
        if (general.Count > 0)
        {
            double total = general.Sum(f => f.Status switch
            {
                ValidationStatus.Fail when dampenDoi && f.ValidatorName == "DoiFormatValidator"
                    => DampenedDoiFailWeight,
                ValidationStatus.Fail => FailWeight,
                ValidationStatus.Unverified => UnverifiedWeight,
                _ => PassWeight
            });

            baseScore = total / general.Count;
        }

        return Math.Clamp(baseScore + chemAdditiveScore + textIntegrityAdditiveScore + ComputeSeverityAmplification(actionable), 0.0, 1.0);
    }

    // Severity amplification: when the governance layer has assigned an effective
    // severity, escalate risk for High/Critical findings. This is additive and
    // backward-compatible — when Severity is null (legacy path) the contribution is zero.
    private const double CriticalSeverityWeight = 0.40;
    private const double HighSeverityWeight = 0.20;
    private const double CorroborationBoost = 0.05;       // per extra same-kind High/Critical finding
    private const double RepeatedPatternBoost = 0.03;     // per extra same-validator non-Info finding
    private const double CriticalFloor = 0.55;            // any Critical => risk ≥ this floor

    private static double ComputeSeverityAmplification(IReadOnlyList<ValidationFinding> actionable)
    {
        double amp = 0.0;
        bool hasCritical = false;

        // Per-finding severity contribution (capped at 4 critical / 6 high so a
        // pathological run with dozens of equivalents doesn't saturate prematurely).
        int critCounted = 0, highCounted = 0;
        foreach (ValidationFinding f in actionable)
        {
            if (f.Severity is null) continue;
            switch (f.Severity)
            {
                case Severity.Critical:
                    hasCritical = true;
                    if (critCounted++ < 4) amp += CriticalSeverityWeight;
                    break;
                case Severity.High:
                    if (highCounted++ < 6) amp += HighSeverityWeight;
                    break;
            }
        }

        // Corroboration: multiple High/Critical findings of the *same* kind
        // reinforce the underlying issue (e.g. several BP exceedances).
        var sameKindGroups = actionable
            .Where(f => f.Severity is Severity.High or Severity.Critical && f.Kind is not null)
            .GroupBy(f => f.Kind!, StringComparer.Ordinal);
        foreach (var g in sameKindGroups)
        {
            int extra = g.Count() - 1;
            if (extra > 0) amp += Math.Min(0.20, extra * CorroborationBoost);
        }

        // Repeated-pattern: a single validator firing many non-Info findings is
        // itself a quality signal even when each individual finding is Medium.
        var sameValidatorGroups = actionable
            .Where(f => f.Severity is not null and not Severity.Info)
            .GroupBy(f => f.ValidatorName, StringComparer.Ordinal);
        foreach (var g in sameValidatorGroups)
        {
            int extra = g.Count() - 1;
            if (extra > 0) amp += Math.Min(0.15, extra * RepeatedPatternBoost);
        }

        // Critical floor: a confirmed physical impossibility should always
        // outweigh tens of low-severity formatting issues.
        if (hasCritical)
        {
            amp = Math.Max(amp, CriticalFloor);
        }

        return amp;
    }
}

