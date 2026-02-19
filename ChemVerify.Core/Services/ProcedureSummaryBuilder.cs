using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Services;

/// <summary>
/// Builds a <see cref="ProcedureSummaryDto"/> from segmented steps, extracted claims,
/// and procedural context.  Pure logic — no I/O, no AI.
/// </summary>
public static class ProcedureSummaryBuilder
{
    private const int MaxSnippetLength = 120;

    private static readonly HashSet<string> ConditionKeys =
        new(StringComparer.OrdinalIgnoreCase) { "temp", "time" };

    private static readonly Regex BranchingLanguageRegex = new(
        @"\b(alternativ\w*|in\s+a\s+separate\s+experiment|route\s+[A-Z/]|pathway\s+[A-Z/]|method\s+[A-Z/]|protocol\s+[A-Z/]|condition\s+set)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ProcedureSummaryDto Build(
        string analyzedText,
        IReadOnlyList<TextStep> steps,
        IReadOnlyList<ExtractedClaim> claims,
        ProceduralContext context,
        IReadOnlyDictionary<int, StepRole>? stepRoles = null)
    {
        ProcedureSummaryDto dto = new()
        {
            IsProcedural = context.IsProcedural,
            ReferencesStartOffset = context.ReferencesStartOffset
        };

        if (string.IsNullOrEmpty(analyzedText) || steps.Count == 0)
            return dto;

        BuildStepSummaries(dto, analyzedText, steps, claims, stepRoles);
        BuildClusters(dto, claims);
        BuildTopIssues(dto, context, analyzedText, steps);

        return dto;
    }

    // ── Step summaries ──────────────────────────────────────────────────

    private static void BuildStepSummaries(
        ProcedureSummaryDto dto,
        string text,
        IReadOnlyList<TextStep> steps,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyDictionary<int, StepRole>? stepRoles)
    {
        foreach (TextStep step in steps)
        {
            int length = Math.Min(MaxSnippetLength, step.EndOffset - step.StartOffset);
            string snippet = text.Substring(step.StartOffset, length).Trim();
            if (length < step.EndOffset - step.StartOffset)
                snippet += "\u2026";

            string? role = stepRoles is not null && stepRoles.TryGetValue(step.Index, out StepRole r)
                ? r.ToString()
                : null;

            List<ClaimRefDto> stepClaims = claims
                .Where(c => c.StepIndex == step.Index)
                .Select(c => new ClaimRefDto
                {
                    ClaimType = c.ClaimType.ToString(),
                    RawText = c.RawText,
                    NormalizedValue = c.NormalizedValue,
                    Unit = c.Unit,
                    ContextKey = GetContextKey(c)
                })
                .ToList();

            dto.Steps.Add(new StepSummaryDto
            {
                StepIndex = step.Index,
                StepSnippet = snippet,
                Role = role,
                Claims = stepClaims
            });
        }
    }

    // ── Condition clustering ────────────────────────────────────────────

    private static void BuildClusters(
        ProcedureSummaryDto dto,
        IReadOnlyList<ExtractedClaim> claims)
    {
        // Group condition claims (temp/time) by step
        Dictionary<int, List<ExtractedClaim>> conditionsByStep = claims
            .Where(c => c.StepIndex.HasValue
                     && c.ClaimType == ClaimType.NumericWithUnit
                     && ConditionKeys.Contains(GetContextKey(c)))
            .GroupBy(c => c.StepIndex!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (conditionsByStep.Count == 0)
            return;

        // Build a canonical signature per step for grouping
        Dictionary<int, string> stepSignatures = new();
        Dictionary<int, Dictionary<string, string>> stepDisplaySigs = new();

        foreach ((int stepIdx, List<ExtractedClaim> stepClaims) in conditionsByStep)
        {
            SortedDictionary<string, string> sigParts = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> displayParts = new();

            foreach (ExtractedClaim claim in stepClaims)
            {
                string ctx = GetContextKey(claim);
                if (!double.TryParse(claim.NormalizedValue, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double raw))
                    continue;

                double normalized = UnitNormalizer.NormalizeValue(claim.Unit ?? string.Empty, raw);
                string canonicalUnit = UnitNormalizer.GetCanonicalUnit(claim.Unit ?? string.Empty);

                sigParts.TryAdd(ctx, $"{normalized:F2}_{canonicalUnit}");
                displayParts.TryAdd(ctx, claim.RawText);
            }

            if (sigParts.Count > 0)
            {
                stepSignatures[stepIdx] = string.Join("|",
                    sigParts.Select(kv => $"{kv.Key}:{kv.Value}"));
                stepDisplaySigs[stepIdx] = displayParts;
            }
        }

        // Group steps by signature
        List<IGrouping<string, KeyValuePair<int, string>>> groups = stepSignatures
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(kv => kv.Key))
            .ToList();

        if (groups.Count < 2)
            return; // Single cluster — nothing interesting to report

        // Suppress clusters when there are 3+ groups and every group is a
        // singleton — this pattern is expected for multistep synthesis where each
        // step has unique conditions. Two-group singletons (A vs B) may still
        // indicate a meaningful dichotomy and are preserved.
        bool allSingletons = groups.All(g => g.Count() == 1);
        if (allSingletons && groups.Count >= 3)
            return;

        char label = 'A';
        foreach (IGrouping<string, KeyValuePair<int, string>> group in groups)
        {
            List<int> stepIndexes = group.Select(kv => kv.Key).OrderBy(i => i).ToList();
            int firstStep = stepIndexes[0];

            dto.Clusters.Add(new ConditionClusterDto
            {
                Label = $"Cluster {label}",
                Signature = stepDisplaySigs[firstStep],
                StepIndexes = stepIndexes
            });
            label++;
        }
    }

    // ── Top issues (max 3) ──────────────────────────────────────────────

    private static void BuildTopIssues(
        ProcedureSummaryDto dto,
        ProceduralContext context,
        string text,
        IReadOnlyList<TextStep> steps)
    {
        if (dto.Clusters.Count < 2)
            return;

        // ── Multi-regime issue ──────────────────────────────
        double confidence = 0.5;
        List<string> why = [];

        if (context.IsProcedural)
        {
            confidence += 0.2;
            why.Add("Text is procedural (lab-action verbs or \u22654 steps).");
        }

        if (dto.Clusters.All(c => c.StepIndexes.Count >= 2))
        {
            confidence += 0.2;
            why.Add("All clusters span \u22652 steps each.");
        }

        if (BranchingLanguageRegex.IsMatch(text))
        {
            confidence -= 0.2;
            why.Add("Branching language detected (e.g., \u2018alternatively\u2019, \u2018route A\u2019).");
        }

        if (dto.Clusters.Any(c => c.StepIndexes.Count == 1))
        {
            confidence -= 0.2;
            why.Add("One cluster appears in only a single step.");
        }

        // Penalize if any cluster's steps fall after the references boundary
        if (context.ReferencesStartOffset.HasValue)
        {
            bool anyInReferences = dto.Clusters
                .SelectMany(c => c.StepIndexes)
                .Any(si =>
                {
                    TextStep step = steps.FirstOrDefault(s => s.Index == si);
                    return step.StartOffset >= context.ReferencesStartOffset.Value;
                });

            if (anyInReferences)
            {
                confidence -= 0.2;
                why.Add("Some claims appear in/after the references section.");
            }
        }

        confidence = Math.Clamp(confidence, 0.0, 1.0);

        string severity = confidence switch
        {
            >= 0.7 => "High",
            >= 0.4 => "Medium",
            _ => "Low"
        };

        if (why.Count == 0)
            why.Add("Multiple distinct condition signatures detected across steps.");

        dto.TopIssues.Add(new TopIssueDto
        {
            Severity = severity,
            Title = $"Detected {dto.Clusters.Count} condition clusters",
            Confidence = Math.Round(confidence, 2),
            Why = why,
            Evidence = new TopIssueEvidenceDto
            {
                StepIndex = dto.Clusters[0].StepIndexes[0],
                Snippet = dto.Steps
                    .FirstOrDefault(s => s.StepIndex == dto.Clusters[0].StepIndexes[0])
                    ?.StepSnippet
            }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    internal static string GetContextKey(ExtractedClaim claim)
    {
        if (string.IsNullOrWhiteSpace(claim.JsonPayload))
            return string.Empty;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(claim.JsonPayload);
            if (doc.RootElement.TryGetProperty("contextKey", out JsonElement prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Malformed JSON — fall through
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns a mapping from step index → cluster label (e.g., "Cluster A").
    /// Steps that don't belong to any cluster are absent from the dictionary.
    /// Used by validators to scope contradictions to same-cluster comparisons.
    /// </summary>
    public static IReadOnlyDictionary<int, string> BuildStepClusterMap(
        IReadOnlyList<ExtractedClaim> claims)
    {
        Dictionary<int, string> map = new();

        Dictionary<int, List<ExtractedClaim>> conditionsByStep = claims
            .Where(c => c.StepIndex.HasValue
                     && c.ClaimType == ClaimType.NumericWithUnit
                     && ConditionKeys.Contains(GetContextKey(c)))
            .GroupBy(c => c.StepIndex!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (conditionsByStep.Count == 0)
            return map;

        Dictionary<int, string> stepSignatures = new();

        foreach ((int stepIdx, List<ExtractedClaim> stepClaims) in conditionsByStep)
        {
            SortedDictionary<string, string> sigParts = new(StringComparer.OrdinalIgnoreCase);

            foreach (ExtractedClaim claim in stepClaims)
            {
                string ctx = GetContextKey(claim);
                if (!double.TryParse(claim.NormalizedValue, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double raw))
                    continue;

                double normalized = UnitNormalizer.NormalizeValue(claim.Unit ?? string.Empty, raw);
                string canonicalUnit = UnitNormalizer.GetCanonicalUnit(claim.Unit ?? string.Empty);

                sigParts.TryAdd(ctx, $"{normalized:F2}_{canonicalUnit}");
            }

            if (sigParts.Count > 0)
            {
                stepSignatures[stepIdx] = string.Join("|",
                    sigParts.Select(kv => $"{kv.Key}:{kv.Value}"));
            }
        }

        List<IGrouping<string, KeyValuePair<int, string>>> groups = stepSignatures
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(kv => kv.Key))
            .ToList();

        if (groups.Count < 2)
            return map; // Single cluster — no scoping needed

        char label = 'A';
        foreach (IGrouping<string, KeyValuePair<int, string>> group in groups)
        {
            string clusterLabel = $"Cluster {label}";
            foreach (KeyValuePair<int, string> kv in group)
                map[kv.Key] = clusterLabel;
            label++;
        }

        return map;
    }

    public static string FormatStepRange(List<int> stepIndexes)
    {
        if (stepIndexes.Count == 0) return "";
        if (stepIndexes.Count == 1) return $"Step {stepIndexes[0]}";

        bool consecutive = true;
        for (int i = 1; i < stepIndexes.Count; i++)
        {
            if (stepIndexes[i] != stepIndexes[i - 1] + 1)
            {
                consecutive = false;
                break;
            }
        }

        return consecutive
            ? $"Steps {stepIndexes[0]}\u2013{stepIndexes[^1]}"
            : "Steps " + string.Join(", ", stepIndexes);
    }
}
