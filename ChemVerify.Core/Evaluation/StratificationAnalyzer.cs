using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Evaluation;

namespace ChemVerify.Core.Evaluation;

/// <summary>
/// Computes disaggregated metrics across multiple dimensions to reveal where
/// validators work well and where they struggle.
/// </summary>
/// <remarks>
/// Stratification dimensions:
/// <list type="bullet">
///   <item><b>ValidatorName</b> — per-validator breakdown (redundant with audit results but included for CSV consistency).</item>
///   <item><b>DomainTag</b> — chemistry domain (e.g. organometallic, esterification).</item>
///   <item><b>ProcedureLengthBucket</b> — short / medium / long based on character count.</item>
///   <item><b>ClaimCountBucket</b> — few / moderate / many based on extracted claim count.</item>
///   <item><b>SourceDataset</b> — origin of the gold-set item.</item>
/// </list>
/// </remarks>
public sealed class StratificationAnalyzer
{
    // Procedure length buckets (character count thresholds)
    private const int ShortProcedureLimit = 500;
    private const int MediumProcedureLimit = 2000;

    // Claim count buckets
    private const int FewClaimsLimit = 5;
    private const int ModerateClaimsLimit = 20;

    /// <summary>
    /// Analyzes raw evaluation results and produces metrics for each stratification subgroup.
    /// </summary>
    public IReadOnlyList<StratifiedMetrics> Analyze(IReadOnlyList<GoldSetRunResult> rawResults)
    {
        ArgumentNullException.ThrowIfNull(rawResults);

        List<StratifiedMetrics> all = [];

        // Dimension: DomainTag
        var byDomainTag = rawResults
            .Where(r => r.Item.DomainTags is { Count: > 0 })
            .SelectMany(r => r.Item.DomainTags!.Select(tag => (Tag: tag, Result: r)))
            .GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byDomainTag)
        {
            var items = group.Select(g => g.Result).ToList();
            all.Add(BuildMetrics("DomainTag", group.Key, items));
        }

        // Dimension: ProcedureLengthBucket
        foreach (var group in rawResults.GroupBy(r => ClassifyLength(r.Item.ParagraphText)))
        {
            all.Add(BuildMetrics("ProcedureLengthBucket", group.Key, group.ToList()));
        }

        // Dimension: ClaimCountBucket
        foreach (var group in rawResults.GroupBy(r => ClassifyClaimCount(r.Claims.Count)))
        {
            all.Add(BuildMetrics("ClaimCountBucket", group.Key, group.ToList()));
        }

        // Dimension: SourceDataset
        var bySource = rawResults
            .Where(r => !string.IsNullOrWhiteSpace(r.Item.SourceDataset))
            .GroupBy(r => r.Item.SourceDataset!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in bySource)
        {
            all.Add(BuildMetrics("SourceDataset", group.Key, group.ToList()));
        }

        return all;
    }

    private static StratifiedMetrics BuildMetrics(
        string dimension, string value, IReadOnlyList<GoldSetRunResult> results)
    {
        int tp = 0, fp = 0, tn = 0, fn = 0, unverified = 0;

        foreach (GoldSetRunResult result in results)
        {
            foreach (ExpectedFinding ef in result.Item.ExpectedFindings)
            {
                var match = result.Findings.FirstOrDefault(f =>
                    f.ValidatorName == ef.ValidatorName);

                ValidationStatus actual = match?.Status ?? ValidationStatus.Pass;

                if (actual == ValidationStatus.Unverified || ef.ExpectedStatus == ValidationStatus.Unverified)
                {
                    unverified++;
                }
                else if (ef.ExpectedStatus == ValidationStatus.Fail && actual == ValidationStatus.Fail)
                {
                    tp++;
                }
                else if (ef.ExpectedStatus == ValidationStatus.Pass && actual == ValidationStatus.Fail)
                {
                    fp++;
                }
                else if (ef.ExpectedStatus == ValidationStatus.Fail && actual != ValidationStatus.Fail)
                {
                    fn++;
                }
                else
                {
                    tn++;
                }
            }
        }

        return new StratifiedMetrics(
            new StratificationKey(dimension, value),
            new ConfusionMatrix(tp, fp, tn, fn, unverified),
            results.Count);
    }

    private static string ClassifyLength(string text)
    {
        int len = text.Length;
        if (len <= ShortProcedureLimit) return "short";
        if (len <= MediumProcedureLimit) return "medium";
        return "long";
    }

    private static string ClassifyClaimCount(int count)
    {
        if (count <= FewClaimsLimit) return "few";
        if (count <= ModerateClaimsLimit) return "moderate";
        return "many";
    }
}
