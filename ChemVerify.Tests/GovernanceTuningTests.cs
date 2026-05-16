using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Governance;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Chemistry;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Governance;
using ChemVerify.Core.Governance.Benchmarking;
using ChemVerify.Core.Governance.Clustering;
using ChemVerify.Core.Governance.Suppressors;
using ChemVerify.Core.Procedure;
using ChemVerify.Core.Services;
using ChemVerify.Core.Validators.State;

namespace ChemVerify.Tests;

/// <summary>
/// Behavioral tests for the governance precision tuning pass: solvent /
/// temperature suppression, post-quench dryness handling, cluster severity
/// aggregation, nonlinear risk scoring, and benchmark accounting.
/// </summary>
public class GovernanceTuningTests
{
    // ── Helpers ───────────────────────────────────────────────────────
    private static AiRun Run(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    private static IReadOnlyList<ExtractedClaim> Extract(AiRun run)
    {
        ReagentRoleExtractor reagent = new();
        NumericUnitExtractor numeric = new();
        return [.. reagent.Extract(run.Id, run.InputText!), .. numeric.Extract(run.Id, run.InputText!)];
    }

    private static (IReadOnlyList<ValidationFinding> findings, IReadOnlyList<StateSnapshot> snapshots)
        ValidateSolventTemp(string text)
    {
        AiRun run = Run(text);
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        IReadOnlyList<StateSnapshot> snaps = ProcedureStateEngine.Build(text, claims);
        IReadOnlyList<ValidationFinding> findings =
            new SolventTemperatureValidator().Validate(run.Id, claims, run);
        return (findings, snaps);
    }

    private static GovernanceProcessor BuildGovernance() => new(
        suppressors:
        [
            new SealedSystemSolventTempSuppressor(),
            new DrynessPostQuenchSuppressor(),
            new CrossStepVariationSuppressor()
        ],
        adjusters: [],
        severityCalculator: new SeverityCalculator());

    // ── Priority 2: solvent / temperature ─────────────────────────────

    [Fact]
    public void Methanol_At90C_OpenVessel_RemainsHighOrCritical()
    {
        AiRun run = Run("Dissolve the substrate in methanol in an open flask. Heated to 90 °C for 1 h.");
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        IReadOnlyList<ValidationFinding> findings =
            new SolventTemperatureValidator().Validate(run.Id, claims, run);

        ValidationFinding? f = findings.FirstOrDefault(x =>
            x.Kind == FindingKind.SolventTemperatureImplausible);
        Assert.NotNull(f);

        BuildGovernance().Process(run, claims, [f!]);
        Assert.False(f!.IsSuppressed);
        Assert.True(f.Severity is Severity.High or Severity.Critical,
            $"Expected High/Critical, got {f.Severity}");
    }

    [Fact]
    public void Methanol_At90C_SealedTube_IsSuppressed()
    {
        AiRun run = Run("Dissolve the substrate in methanol in a sealed tube. Heated to 90 °C for 1 h.");
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        IReadOnlyList<ValidationFinding> findings =
            new SolventTemperatureValidator().Validate(run.Id, claims, run);

        // Either the validator skips it (closed apparatus) or governance suppresses it.
        if (findings.Count == 0)
        {
            return;
        }

        BuildGovernance().Process(run, claims, findings);

        ValidationFinding f = findings.First(x =>
            x.Kind == FindingKind.SolventTemperatureImplausible);
        Assert.True(f.IsSuppressed,
            $"Expected suppression in sealed tube; got reason={f.SuppressionReasonCode}");
        Assert.Equal(SuppressionReason.SealedOrPressurizedSystem.ToString(), f.SuppressionReasonCode);
    }

    [Fact]
    public void Reflux_InEthanol_IsNotCountedAsSolventTemperatureFailure()
    {
        AiRun run = Run("Dissolve in ethanol. The mixture was heated at reflux for 4 h.");
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        IReadOnlyList<ValidationFinding> findings =
            new SolventTemperatureValidator().Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.SolventTemperatureImplausible);
        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.ImpossibleRefluxCondition);
    }

    [Fact]
    public void Reflux_AtImplausibleTemperature_StillReportsImpossibleReflux()
    {
        // Sanity: validator still catches a genuinely impossible reflux.
        AiRun run = Run("Dissolve in toluene (BP 110 °C). The mixture was refluxed at 30 °C for 2 h.");
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        IReadOnlyList<ValidationFinding> findings =
            new SolventTemperatureValidator().Validate(run.Id, claims, run);

        Assert.Contains(findings, f => f.Kind == FindingKind.ImpossibleRefluxCondition);
    }

    // ── Dryness & post-quench ─────────────────────────────────────────

    [Fact]
    public void Water_AfterQuench_SuppressesAtmosphereContainmentMismatch()
    {
        // Build a snapshot stack where the post-quench phase is active.
        StateSnapshot snap = MakeSnapshot(
            stepIndex: 2,
            phase: ReactionPhase.Workup,
            flags: [ProcedureStateFlags.PostQuench]);

        ValidationFinding f = MakeFinding(
            FindingKind.AmbiguousWorkupTransition,
            stepIndex: 2,
            confidence: 0.8);

        Evaluate(new DrynessPostQuenchSuppressor(), f, [snap]);

        Assert.True(f.IsSuppressed);
        Assert.Equal(SuppressionReason.WorkupTransitionExpected.ToString(), f.SuppressionReasonCode);
    }

    [Fact]
    public void DrynessFinding_AfterQuench_IsDowngradedNotSuppressed()
    {
        StateSnapshot snap = MakeSnapshot(
            stepIndex: 3,
            phase: ReactionPhase.Workup,
            flags: [ProcedureStateFlags.PostQuench]);

        ValidationFinding f = MakeFinding(
            FindingKind.DrynessSemanticContradiction,
            stepIndex: 3,
            confidence: 0.9);

        Evaluate(new DrynessPostQuenchSuppressor(), f, [snap]);

        Assert.False(f.IsSuppressed);
        Assert.NotNull(f.AdjustedConfidence);
        Assert.True(f.AdjustedConfidence < f.Confidence);
        Assert.Equal(SuppressionReason.PostQuenchAqueousExpected.ToString(), f.SuppressionReasonCode);
    }

    [Fact]
    public void DriedWithWater_RemainsHighSeverity_AndIsNotSuppressed()
    {
        AiRun run = Run("The organic layer was dried with water and concentrated.");
        IReadOnlyList<ExtractedClaim> claims = Extract(run);
        IReadOnlyList<ValidationFinding> findings =
            new DrynessConsistencyValidator().Validate(run.Id, claims, run);

        ValidationFinding f = Assert.Single(findings);
        Assert.Equal(FindingKind.DrynessSemanticContradiction, f.Kind);

        // No quench/workup phase has occurred — suppressor must NOT fire.
        BuildGovernance().Process(run, claims, [f]);

        Assert.False(f.IsSuppressed,
            $"'dried with water' should never be suppressed; reason={f.SuppressionReasonCode}");
        Assert.Equal(Severity.High, f.Severity);
    }

    // ── Cross-step variation ──────────────────────────────────────────

    [Fact]
    public void CrossStepTemperatureVariation_IsSuppressedByGovernance()
    {
        ValidationFinding f = MakeFinding(
            FindingKind.Contradiction,
            stepIndex: 1,
            confidence: 0.7,
            validatorName: "NumericContradictionValidator",
            message: "Possible contradiction: temp 0 °C in step 1 vs 60 °C in step 3.");

        ValidatorDecisionContext ctx = new()
        {
            Run = Run("(synthetic)"),
            Claims = [],
            AllFindings = [f],
            Finding = f,
            StateBag = Array.Empty<StateSnapshot>()
        };

        ValidationOutcomeAdjustment adj = new CrossStepVariationSuppressor().Evaluate(ctx);

        Assert.True(adj.Suppress);
        Assert.Equal(SuppressionReason.CrossStepVariationExpected, adj.Reason);
    }

    // ── Clustering ────────────────────────────────────────────────────

    [Fact]
    public void Clusters_AggregateSeverityCorrectly()
    {
        ValidationFinding crit = MakeFinding(FindingKind.PhysicallyImplausibleValue, stepIndex: 1, severity: Severity.Critical);
        ValidationFinding hi   = MakeFinding(FindingKind.SolventTemperatureImplausible, stepIndex: 2, severity: Severity.High);
        ValidationFinding med  = MakeFinding(FindingKind.MissingTemperature, stepIndex: 3, severity: Severity.Medium);
        ValidationFinding low  = MakeFinding(FindingKind.PlaceholderOrMissingToken, stepIndex: 4, severity: Severity.Low);

        IReadOnlyList<FindingCluster> clusters =
            new FindingClusterBuilder().Build([crit, hi, med, low]);

        FindingCluster reactionConditions = clusters.Single(c => c.Theme == "ReactionConditions");
        Assert.Equal(Severity.Critical, reactionConditions.AggregatedSeverity);
        Assert.Equal(3, reactionConditions.FindingCount);
        Assert.Equal(1, reactionConditions.CriticalCount);
        Assert.Equal(1, reactionConditions.HighCount);
        Assert.True(reactionConditions.RiskContribution > 0);

        FindingCluster textIntegrity = clusters.Single(c => c.Theme == "TextIntegrity");
        Assert.Equal(Severity.Low, textIntegrity.AggregatedSeverity);
        Assert.Equal(1, textIntegrity.FindingCount);

        // Clusters are ordered by descending risk contribution.
        Assert.True(clusters[0].RiskContribution >= clusters[^1].RiskContribution);

        // ClusterId is propagated onto the findings.
        Assert.Equal(reactionConditions.Id, crit.ClusterId);
        Assert.Equal(textIntegrity.Id, low.ClusterId);
    }

    [Fact]
    public void Clusters_OmitDiagnosticAndSuppressedFindings()
    {
        ValidationFinding diag = MakeFinding(FindingKind.WorkupTransitionDetected, stepIndex: 1);
        diag.Category = FindingCategory.Diagnostic;
        ValidationFinding suppressed = MakeFinding(FindingKind.AmbiguousWorkupTransition, stepIndex: 2);
        suppressed.IsSuppressed = true;
        ValidationFinding active = MakeFinding(FindingKind.SolventTemperatureImplausible, stepIndex: 3, severity: Severity.High);

        IReadOnlyList<FindingCluster> clusters =
            new FindingClusterBuilder().Build([diag, suppressed, active]);

        FindingCluster only = Assert.Single(clusters);
        Assert.Equal("ReactionConditions", only.Theme);
        Assert.Equal(1, only.FindingCount);
    }

    // ── Risk scoring nonlinearity ─────────────────────────────────────

    [Fact]
    public void RiskScore_OneCritical_OutweighsManyLow()
    {
        RiskScorer scorer = new();

        List<ValidationFinding> critical =
        [
            MakeFinding(FindingKind.PhysicallyImplausibleValue, stepIndex: 1,
                severity: Severity.Critical, status: ValidationStatus.Fail, confidence: 0.95)
        ];

        List<ValidationFinding> manyLow = [];
        for (int i = 0; i < 30; i++)
        {
            // MultiScenario is a Low-severity kind that lives in neither the
            // chemistry nor text-integrity additive buckets, so it exercises
            // the "many low signals" path without saturating the score.
            manyLow.Add(MakeFinding(FindingKind.MultiScenario, stepIndex: i,
                severity: Severity.Low, status: ValidationStatus.Unverified, confidence: 0.4));
        }

        double critScore = scorer.ComputeScore(critical);
        double lowScore = scorer.ComputeScore(manyLow);

        Assert.True(critScore >= 0.55, $"Critical floor not honored: {critScore}");
        Assert.True(critScore > lowScore,
            $"One Critical ({critScore:0.000}) should outweigh many Low ({lowScore:0.000}).");
    }

    [Fact]
    public void RiskScore_SuppressedFindings_AreExcluded()
    {
        RiskScorer scorer = new();

        ValidationFinding suppressed = MakeFinding(
            FindingKind.SolventTemperatureImplausible,
            stepIndex: 1, severity: Severity.High, status: ValidationStatus.Fail);
        suppressed.IsSuppressed = true;

        Assert.Equal(0.0, scorer.ComputeScore([suppressed]));
    }

    // ── Benchmark counts ──────────────────────────────────────────────

    [Fact]
    public void BenchmarkSummary_ReportsRawAdjustedSuppressedDowngradedCounts()
    {
        ValidationFinding active = MakeFinding(FindingKind.SolventTemperatureImplausible, stepIndex: 1,
            severity: Severity.High, validatorName: "SolventTemperatureValidator");
        ValidationFinding downgraded = MakeFinding(FindingKind.DrynessSemanticContradiction, stepIndex: 2,
            severity: Severity.Medium, validatorName: "DrynessConsistencyValidator", confidence: 0.9);
        downgraded.AdjustedConfidence = 0.45;
        ValidationFinding suppressed = MakeFinding(FindingKind.AmbiguousWorkupTransition, stepIndex: 3,
            severity: Severity.Medium, validatorName: "WorkupTransitionValidator");
        suppressed.IsSuppressed = true;
        ValidationFinding diagnostic = MakeFinding(FindingKind.WorkupTransitionDetected, stepIndex: 4,
            validatorName: "DiagnosticValidator");
        diagnostic.Category = FindingCategory.Diagnostic;

        BenchmarkSummary summary = new BenchmarkSummaryGenerator()
            .Generate([active, downgraded, suppressed, diagnostic]);

        Assert.Equal(4, summary.TotalFindings);
        Assert.Equal(2, summary.ActiveFindings);
        Assert.Equal(1, summary.SuppressedFindings);
        Assert.Equal(1, summary.DowngradedFindings);
        Assert.Equal(1, summary.DiagnosticFindings);

        ValidatorPrecisionMetric? supMetric = summary.ValidatorLeaderboard
            .FirstOrDefault(m => m.ValidatorName == "WorkupTransitionValidator");
        Assert.NotNull(supMetric);
        Assert.Equal(1.0, supMetric!.SuppressionRate);
        Assert.Equal(0.0, supMetric.PrecisionEstimate);
    }

    // ── Report governance overlay ─────────────────────────────────────

    [Fact]
    public void ReportBuilder_OmitsGovernanceWhenEmpty()
    {
        ReportDto report = ReportBuilder.Build(0.0, [], []);
        Assert.Null(report.Governance);
    }

    [Fact]
    public void ReportBuilder_BuildWithGovernance_PopulatesOverlay()
    {
        ValidationFinding hi = MakeFinding(FindingKind.SolventTemperatureImplausible,
            stepIndex: 1, severity: Severity.High, status: ValidationStatus.Fail);
        ValidationFinding suppressed = MakeFinding(FindingKind.AmbiguousWorkupTransition,
            stepIndex: 2, severity: Severity.Medium, status: ValidationStatus.Fail);
        suppressed.IsSuppressed = true;
        suppressed.SuppressionReasonCode = SuppressionReason.WorkupTransitionExpected.ToString();
        suppressed.AdjustmentExplanation = "post-quench expected";

        ReportDto report = ReportBuilder.BuildWithGovernance(
            riskScore: 0.5,
            claims: [],
            findings: [hi, suppressed],
            snapshots: null);

        Assert.NotNull(report.Governance);
        Assert.NotEmpty(report.Governance!.SeverityHistogram);
        Assert.NotEmpty(report.Governance.Clusters);
        Assert.Contains(report.Governance.Suppressions,
            s => s.Reason == SuppressionReason.WorkupTransitionExpected.ToString());
    }

    // ── Test fixtures ─────────────────────────────────────────────────

    private static ValidationFinding MakeFinding(
        string kind,
        int stepIndex,
        double confidence = 0.8,
        Severity? severity = null,
        ValidationStatus status = ValidationStatus.Fail,
        string validatorName = "TestValidator",
        string? message = null) => new()
    {
        Id = Guid.NewGuid(),
        RunId = Guid.NewGuid(),
        ValidatorName = validatorName,
        Status = status,
        Message = message ?? $"[TEST] {kind}",
        Confidence = confidence,
        Kind = kind,
        EvidenceStepIndex = stepIndex,
        Severity = severity
    };

    private static StateSnapshot MakeSnapshot(
        int stepIndex,
        ReactionPhase phase,
        IEnumerable<string>? flags = null)
    {
        ProcedureState after = new()
        {
            StepIndex = stepIndex,
            CurrentPhase = phase,
            CurrentFlags = new HashSet<string>(flags ?? [], StringComparer.OrdinalIgnoreCase)
        };
        return new StateSnapshot(stepIndex, 0, 0, "(synthetic)", new ProcedureState(), after, []);
    }

    private static void Evaluate(
        IFindingSuppressor suppressor,
        ValidationFinding finding,
        IReadOnlyList<StateSnapshot> snapshots)
    {
        ValidatorDecisionContext ctx = new()
        {
            Run = Run("(synthetic)"),
            Claims = [],
            AllFindings = [finding],
            Finding = finding,
            StateBag = snapshots
        };

        ValidationOutcomeAdjustment adj = suppressor.Evaluate(ctx);
        if (adj.Reason == SuppressionReason.None) return;

        finding.SuppressionReasonCode = adj.Reason.ToString();
        finding.AdjustmentExplanation = adj.Explanation;
        if (adj.Suppress)
        {
            finding.IsSuppressed = true;
            return;
        }
        if (adj.ConfidenceMultiplier < 1.0)
        {
            finding.AdjustedConfidence = Math.Clamp(
                finding.Confidence * adj.ConfidenceMultiplier, 0.0, 1.0);
        }
    }
}
