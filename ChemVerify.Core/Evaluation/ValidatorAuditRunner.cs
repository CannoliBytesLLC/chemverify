using System.Text.Json;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Evaluation;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Evaluation;

/// <summary>
/// Runs all registered validators against a gold-set dataset, computes per-validator
/// confusion matrices, confidence calibration, stratified metrics, a review queue,
/// and a ranked leaderboard.
/// </summary>
/// <remarks>
/// Design decisions:
/// <list type="bullet">
///   <item>Uses the real extraction pipeline by default (end-to-end test).
///         If a gold-set item includes pre-serialized claims, those are used instead
///         so validators can be tested in isolation.</item>
///   <item>Each gold-set item is run through the full validator set once.
///         Results are then compared against <see cref="ExpectedFinding"/> entries.</item>
///   <item>Delegates confidence calibration to <see cref="ConfidenceCalibrationAnalyzer"/>
///         and review queue generation to <see cref="ReviewQueueBuilder"/>.</item>
/// </list>
/// </remarks>
public sealed class ValidatorAuditRunner : IValidatorAuditRunner
{
    private readonly IEnumerable<IValidator> _validators;
    private readonly IClaimExtractor _extractor;
    private readonly ConfidenceCalibrationAnalyzer _calibrationAnalyzer;
    private readonly ReviewQueueBuilder _reviewQueueBuilder;
    private readonly StratificationAnalyzer _stratificationAnalyzer;

    /// <summary>Maximum misclassified examples to capture per category per validator.</summary>
    private const int MaxExamplesPerCategory = 25;

    /// <summary>Snippet length for paragraph text in misclassified examples.</summary>
    private const int SnippetLength = 200;

    public ValidatorAuditRunner(
        IEnumerable<IValidator> validators,
        IClaimExtractor extractor,
        ConfidenceCalibrationAnalyzer calibrationAnalyzer,
        ReviewQueueBuilder reviewQueueBuilder,
        StratificationAnalyzer stratificationAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(validators);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(calibrationAnalyzer);
        ArgumentNullException.ThrowIfNull(reviewQueueBuilder);
        ArgumentNullException.ThrowIfNull(stratificationAnalyzer);

        _validators = validators;
        _extractor = extractor;
        _calibrationAnalyzer = calibrationAnalyzer;
        _reviewQueueBuilder = reviewQueueBuilder;
        _stratificationAnalyzer = stratificationAnalyzer;
    }

    /// <inheritdoc/>
    public EvaluationSummary Evaluate(IReadOnlyList<GoldSetItem> goldSet)
    {
        ArgumentNullException.ThrowIfNull(goldSet);

        // Phase 1: Run all validators on every gold-set item and collect raw results
        List<GoldSetRunResult> rawResults = [];
        foreach (GoldSetItem item in goldSet)
        {
            (IReadOnlyList<ExtractedClaim> claims, IReadOnlyList<ValidationFinding> findings) = RunValidators(item);
            rawResults.Add(new GoldSetRunResult(item, claims, findings));
        }

        // Phase 2: Compute per-validator audit results
        string[] validatorNames = rawResults
            .SelectMany(r => r.Item.ExpectedFindings.Select(ef => ef.ValidatorName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        List<ValidatorAuditResult> validatorResults = [];
        foreach (string validatorName in validatorNames)
        {
            ValidatorAuditResult result = EvaluateValidator(validatorName, rawResults);
            validatorResults.Add(result);
        }

        // Phase 3: Stratified analysis
        IReadOnlyList<StratifiedMetrics> stratified = _stratificationAnalyzer.Analyze(rawResults);

        // Phase 4: Review queue
        IReadOnlyList<ReviewQueueItem> reviewQueue = _reviewQueueBuilder.Build(rawResults, validatorResults);

        // Phase 5: Leaderboard
        IReadOnlyList<LeaderboardEntry> leaderboard = BuildLeaderboard(validatorResults);

        return new EvaluationSummary(
            RunTimestamp: DateTimeOffset.UtcNow,
            GoldSetCount: goldSet.Count,
            ValidatorResults: validatorResults,
            StratifiedResults: stratified,
            ReviewQueue: reviewQueue,
            Leaderboard: leaderboard);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private (IReadOnlyList<ExtractedClaim> Claims, IReadOnlyList<ValidationFinding> Findings) RunValidators(GoldSetItem item)
    {
        Guid runId = Guid.NewGuid();

        // Build a synthetic AiRun to drive validators
        AiRun run = new()
        {
            Id = runId,
            CreatedUtc = DateTimeOffset.UtcNow,
            Status = RunStatus.Completed,
            Mode = RunMode.VerifyOnly,
            ModelName = "gold-set-evaluation",
            InputText = item.ParagraphText,
            CurrentHash = string.Empty
        };

        // Extract claims (or use pre-serialized ones from the gold-set item)
        IReadOnlyList<ExtractedClaim> claims;
        if (!string.IsNullOrWhiteSpace(item.ClaimsJson))
        {
            claims = JsonSerializer.Deserialize<List<ExtractedClaim>>(item.ClaimsJson) ?? [];
            // Patch run IDs so validators see consistent data
            foreach (ExtractedClaim c in claims)
                c.RunId = runId;
        }
        else
        {
            claims = _extractor.Extract(runId, item.ParagraphText);
        }

        // Run all validators
        List<ValidationFinding> allFindings = [];
        foreach (IValidator validator in _validators)
        {
            IReadOnlyList<ValidationFinding> findings = validator.Validate(runId, claims, run);
            allFindings.AddRange(findings);
        }

        return (claims, allFindings);
    }

    private ValidatorAuditResult EvaluateValidator(string validatorName, List<GoldSetRunResult> rawResults)
    {
        List<ClassifiedOutcome> outcomes = [];

        foreach (GoldSetRunResult result in rawResults)
        {
            // Get expected findings for this validator from this gold-set item
            var expected = result.Item.ExpectedFindings
                .Where(ef => ef.ValidatorName == validatorName)
                .ToList();

            // Get actual findings from this validator
            var actual = result.Findings
                .Where(f => f.ValidatorName == validatorName)
                .ToList();

            if (expected.Count == 0 && actual.Count == 0)
                continue; // This validator was not expected to fire and didn't — skip entirely

            if (expected.Count == 0)
            {
                // Validator fired but was not expected — each finding is a FP
                foreach (ValidationFinding f in actual)
                {
                    outcomes.Add(new ClassifiedOutcome(
                        result.Item, f.Status, ValidationStatus.Pass,
                        f.Confidence, f.Message, f.Kind));
                }
                continue;
            }

            // Match expected findings against actual findings
            foreach (ExpectedFinding ef in expected)
            {
                // Find best matching actual finding
                ValidationFinding? match = actual.FirstOrDefault(f =>
                    ef.ExpectedKind is null || f.Kind == ef.ExpectedKind);

                if (match is null)
                {
                    // Expected finding not produced — FN if expected Fail, TN if expected Pass
                    outcomes.Add(new ClassifiedOutcome(
                        result.Item,
                        ActualStatus: ValidationStatus.Pass, // validator didn't fire
                        ExpectedStatus: ef.ExpectedStatus,
                        Confidence: 0.0,
                        Message: null,
                        Kind: ef.ExpectedKind));
                }
                else
                {
                    outcomes.Add(new ClassifiedOutcome(
                        result.Item, match.Status, ef.ExpectedStatus,
                        match.Confidence, match.Message, match.Kind));
                }
            }
        }

        // Build confusion matrix
        int tp = outcomes.Count(o => o.ExpectedStatus == ValidationStatus.Fail && o.ActualStatus == ValidationStatus.Fail);
        int fp = outcomes.Count(o => o.ExpectedStatus == ValidationStatus.Pass && o.ActualStatus == ValidationStatus.Fail);
        int tn = outcomes.Count(o => o.ExpectedStatus == ValidationStatus.Pass && o.ActualStatus != ValidationStatus.Fail);
        int fn = outcomes.Count(o => o.ExpectedStatus == ValidationStatus.Fail && o.ActualStatus != ValidationStatus.Fail
                                     && o.ActualStatus != ValidationStatus.Unverified);
        int unverified = outcomes.Count(o => o.ActualStatus == ValidationStatus.Unverified
                                             || o.ExpectedStatus == ValidationStatus.Unverified);

        ConfusionMatrix matrix = new(tp, fp, tn, fn, unverified);

        // Confidence calibration
        ConfidenceCalibration calibration = _calibrationAnalyzer.Analyze(outcomes);

        // Top misclassified examples
        IReadOnlyList<MisclassifiedExample> topFP = outcomes
            .Where(o => o.ExpectedStatus == ValidationStatus.Pass && o.ActualStatus == ValidationStatus.Fail)
            .OrderByDescending(o => o.Confidence)
            .Take(MaxExamplesPerCategory)
            .Select(o => ToMisclassified(o))
            .ToList();

        IReadOnlyList<MisclassifiedExample> topFN = outcomes
            .Where(o => o.ExpectedStatus == ValidationStatus.Fail && o.ActualStatus != ValidationStatus.Fail)
            .OrderByDescending(o => o.Confidence)
            .Take(MaxExamplesPerCategory)
            .Select(o => ToMisclassified(o))
            .ToList();

        IReadOnlyList<MisclassifiedExample> topAmbiguous = outcomes
            .Where(o => o.ActualStatus == ValidationStatus.Unverified)
            .OrderByDescending(o => o.Confidence)
            .Take(MaxExamplesPerCategory)
            .Select(o => ToMisclassified(o))
            .ToList();

        return new ValidatorAuditResult(
            validatorName, matrix, calibration, topFP, topFN, topAmbiguous);
    }

    private static MisclassifiedExample ToMisclassified(ClassifiedOutcome o)
    {
        string snippet = o.Item.ParagraphText.Length <= SnippetLength
            ? o.Item.ParagraphText
            : string.Concat(o.Item.ParagraphText.AsSpan(0, SnippetLength), "…");

        return new MisclassifiedExample(
            GoldSetItemId: o.Item.Id,
            ParagraphSnippet: snippet,
            ActualStatus: o.ActualStatus,
            ExpectedStatus: o.ExpectedStatus,
            Confidence: o.Confidence,
            FindingMessage: o.Message,
            FindingKind: o.Kind);
    }

    private static IReadOnlyList<LeaderboardEntry> BuildLeaderboard(
        IReadOnlyList<ValidatorAuditResult> results)
    {
        return results
            .OrderByDescending(r => r.Matrix.F1Score ?? 0.0)
            .ThenByDescending(r => r.Matrix.Precision ?? 0.0)
            .Select((r, i) => new LeaderboardEntry(
                Rank: i + 1,
                ValidatorName: r.ValidatorName,
                Precision: r.Matrix.Precision,
                Recall: r.Matrix.Recall,
                F1Score: r.Matrix.F1Score,
                TotalEvaluated: r.Matrix.Total,
                FalsePositiveCount: r.Matrix.FalsePositives,
                UnverifiedCount: r.Matrix.UnverifiedCount))
            .ToList();
    }
}

// ── Result types ────────────────────────────────────────────────────────────

/// <summary>
/// Raw result of running all validators on a single gold-set item.
/// </summary>
public sealed record GoldSetRunResult(
    GoldSetItem Item,
    IReadOnlyList<ExtractedClaim> Claims,
    IReadOnlyList<ValidationFinding> Findings);

/// <summary>
/// A single finding outcome classified against the gold-set expectation.
/// </summary>
public sealed record ClassifiedOutcome(
    GoldSetItem Item,
    ValidationStatus ActualStatus,
    ValidationStatus ExpectedStatus,
    double Confidence,
    string? Message,
    string? Kind);
