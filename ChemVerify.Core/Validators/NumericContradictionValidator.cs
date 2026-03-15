using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;
using ChemVerify.Core.Validation;

namespace ChemVerify.Core.Validators;

public class NumericContradictionValidator : IValidator
{
    private const double ContradictionThresholdPercent = 50.0;
    private const double ConsistencyThresholdPercent = 5.0;

    private static readonly Regex MultiScenarioRegex = new(
        @"\b(alternativ\w*|route|separate\w*|trial|condition\s*set|variant|respective\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AdditiveCueRegex = new(
        @"\b(an?\s+additional|additional|another|for\s+another|then|followed\s+by|and\s+then|after\s+which)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimeOperationRegex = new(
        @"\b(reflux(?:ed|ing)?|stir(?:red|ring)?|heat(?:ed|ing)?|hold|held|incubat(?:ed|ing)?|cool(?:ed|ing)?|warm(?:ed|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ?? Operation-tag inference regexes ??????????????????????????????????
    private static readonly Regex OpStirHoldRegex = new(
        @"\b(stir(?:red|ring)?|maintain(?:ed|ing)?|hold|held|kept)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpHeatRefluxRegex = new(
        @"\b(heat(?:ed|ing)?|reflux(?:ed|ing)?|boil(?:ed|ing)?|at\s+reflux|warm(?:ed|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpAddDoseRegex = new(
        @"\b(add(?:ed|ing)?|addition|dropwise|portionwise|charg(?:ed|ing)?|introduc(?:ed|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpCoolQuenchRegex = new(
        @"\b(cool(?:ed|ing)?|quench(?:ed|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpWaitStandRegex = new(
        @"\b(stand|allow(?:ed)?\s+to\s+stand|overnight|aged?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ?? Checkpoint / cumulative cues ?????????????????????????????????????
    private static readonly Regex CheckpointCueRegex = new(
        @"\b(after|following|once|when|upon)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CumulativeCueRegex = new(
        @"\b(for\s+a\s+total\s+of|total\s+(?:time|of)|in\s+total|overall|total)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ?? Condition-signature regex (temperature tokens near time claims) ???
    private static readonly Regex ConditionTempRegex = new(
        @"-?\d+(?:\.\d+)?\s*(?:�\s*C|deg(?:rees?)?\s*C)\b"
        + @"|\broom\s*temp(?:erature)?\b|\bambient\s*temp(?:erature)?\b"
        + @"|\bice[\s-](?:bath|water\s+bath)\b|\breflux\b"
        + @"|\b[Rr]\.?[Tt]\.?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ?? Chromatography gradient context (for percent claims) ?????????????
    private static readonly Regex ChromatographyContextRegex = new(
        @"\b(chromatograph\w*|column|SiO2|silica|elut(?:ing|ion|ed|ant|ent)|gradient|flash|TLC|HPLC|increasing\s+polarity)\b|\u2192",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ComparableContextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "temp", "time", "yield", "conc", "purity", "impurity"
    };

    private static readonly HashSet<string> ConditionContextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "temp", "time", "conc"
    };

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();

        // Build cluster map for scoping condition (temp/time) comparisons
        IReadOnlyDictionary<int, string> clusterMap = ProcedureSummaryBuilder.BuildStepClusterMap(claims);

        List<ExtractedClaim> numericClaims = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit && c.Unit is not null)
            .ToList();

        // Partition: only claims with a comparable contextKey enter contradiction logic.
        // Non-comparable claims are silently skipped � no diagnostic emission needed
        // (these were historically 80%+ of all validator output with zero actionable value).
        List<ExtractedClaim> comparableClaims = numericClaims
            .Where(c => ComparableContextKeys.Contains(ExtractContextKey(c)))
            .ToList();

        // Group comparable claims by (ContextKey + Unit + EntityKey) for step-scoped comparison
        IEnumerable<IGrouping<string, ExtractedClaim>> groupedByUnit = comparableClaims
            .GroupBy(c => BuildGroupKey(c));

        foreach (IGrouping<string, ExtractedClaim> group in groupedByUnit)
        {
            List<ExtractedClaim> groupList = group.ToList();

            // Singleton groups have nothing to compare � skip silently.
            // (These were historically ~10% of all validator output with zero actionable value.)
            if (groupList.Count < 2)
            {
                continue;
            }

            // Determine if this is a condition group (temp/time) that needs step/cluster scoping
            string groupContextKey = ExtractContextKey(groupList[0]);
            bool isConditionGroup = ConditionContextKeys.Contains(groupContextKey);
            bool crossScopeConflictFound = false;
            bool emittedGroupLevel = false;

            // Compare pairs for contradictions
            for (int i = 0; i < groupList.Count; i++)
            {
                for (int j = i + 1; j < groupList.Count; j++)
                {
                    // Skip pairs in the same step when they have different entity keys
                    // (e.g., "benzaldehyde 1.06 g" and "NaBH4 0.38 g" in the same sentence)
                    if (AreDifferentEntitiesInSameStep(groupList[i], groupList[j]))
                    {
                        continue;
                    }

                    // For time claims, skip pairs that refer to different step actions
                    if (ExtractContextKey(groupList[i]) == "time" &&
                        HasDifferentTimeActions(groupList[i], groupList[j]))
                    {
                        continue;
                    }

                    bool parsedA = double.TryParse(
                        groupList[i].NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double rawA);
                    bool parsedB = double.TryParse(
                        groupList[j].NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double rawB);

                    if (!parsedA || !parsedB)
                    {
                        continue;
                    }

                    // Normalize values to canonical units before comparison
                    double valA = UnitNormalizer.NormalizeValue(groupList[i].Unit ?? string.Empty, rawA);
                    double valB = UnitNormalizer.NormalizeValue(groupList[j].Unit ?? string.Empty, rawB);

                    double average = (Math.Abs(valA) + Math.Abs(valB)) / 2.0;
                    double diff = Math.Abs(valA - valB);
                    bool isContradiction = average > 0 && (diff / average) * 100.0 > ContradictionThresholdPercent;

                    bool crossScope = isConditionGroup && !AreInSameScope(groupList[i], groupList[j], clusterMap);

                    if (isContradiction)
                    {
                        // For condition groups across different scopes, defer to post-loop handling
                        if (crossScope)
                        {
                            crossScopeConflictFound = true;
                            continue;
                        }

                        // Check for multi-scenario language near either claim
                        bool multiScenario = HasMultiScenarioContext(run.GetAnalyzedText(), groupList[i]) ||
                                             HasMultiScenarioContext(run.GetAnalyzedText(), groupList[j]);

                        if (multiScenario)
                        {
                            // Emit one group-level finding instead of duplicating per pair
                            string claimRefs = string.Join("+", groupList.Select(c => $"Claim:{c.Id}"));
                            string rawValues = string.Join(", ", groupList.Select(c => c.RawText));

                            findings.Add(new ValidationFinding
                            {
                                Id = Guid.NewGuid(),
                                RunId = runId,
                                ClaimId = null,
                                ValidatorName = nameof(NumericContradictionValidator),
                                Status = ValidationStatus.Unverified,
                                Message = $"Multiple scenarios detected ({rawValues}); values may refer to different conditions.",
                                Confidence = 0.5,
                                EvidenceRef = claimRefs,
                                Kind = FindingKind.MultiScenario
                            });

                            emittedGroupLevel = true;
                            break;
                        }

                        // Check for sequential durations ("reflux 30 min � an additional 15 min")
                        if (groupContextKey == "time")
                        {
                            string? seqCue = DetectSequentialDuration(run.GetAnalyzedText(), groupList[i], groupList[j]);
                            if (seqCue is not null)
                            {
                                findings.Add(new ValidationFinding
                                {
                                    Id = Guid.NewGuid(),
                                    RunId = runId,
                                    ClaimId = groupList[i].Id,
                                    ValidatorName = nameof(NumericContradictionValidator),
                                    Status = ValidationStatus.Pass,
                                    Message = $"Sequential durations detected ({groupList[i].RawText}, then {groupList[j].RawText}); not contradictory.",
                                    Confidence = 0.85,
                                    EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                    EvidenceSnippet = seqCue,
                                    Kind = FindingKind.SequentialDuration,
                                    Category = FindingCategory.Diagnostic
                                });
                                continue;
                            }

                            // Check checkpoint vs total/cumulative time
                            if (IsCheckpointVsTotal(run.GetAnalyzedText(), groupList[i], groupList[j]))
                            {
                                findings.Add(new ValidationFinding
                                {
                                    Id = Guid.NewGuid(),
                                    RunId = runId,
                                    ClaimId = groupList[i].Id,
                                    ValidatorName = nameof(NumericContradictionValidator),
                                    Status = ValidationStatus.Pass,
                                    Message = $"Checkpoint vs cumulative total ({groupList[i].RawText} / {groupList[j].RawText}); not contradictory.",
                                    Confidence = 0.85,
                                    EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                    Kind = FindingKind.CheckpointVsTotal,
                                    Category = FindingCategory.Diagnostic
                                });
                                continue;
                            }

                            // Check operation-tag mismatch (different sub-operations are not contradictory)
                            string tagI = InferOperationTag(run.GetAnalyzedText(), groupList[i]);
                            string tagJ = InferOperationTag(run.GetAnalyzedText(), groupList[j]);

                            if (tagI.Length > 0 && tagJ.Length > 0
                                && !string.Equals(tagI, tagJ, StringComparison.Ordinal))
                            {
                                findings.Add(new ValidationFinding
                                {
                                    Id = Guid.NewGuid(),
                                    RunId = runId,
                                    ClaimId = groupList[i].Id,
                                    ValidatorName = nameof(NumericContradictionValidator),
                                    Status = ValidationStatus.Pass,
                                    Message = $"Different operations ({tagI} vs {tagJ}): {groupList[i].RawText} / {groupList[j].RawText}; not contradictory.",
                                    Confidence = 0.85,
                                    EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                    Kind = FindingKind.DifferentOperation,
                                    Category = FindingCategory.Diagnostic
                                });
                                continue;
                            }

                            // Check if time claims have different temperature regimes
                            string tempSigI = ExtractNearbyTempSignature(run.GetAnalyzedText(), groupList[i]);
                            string tempSigJ = ExtractNearbyTempSignature(run.GetAnalyzedText(), groupList[j]);

                            if (tempSigI.Length > 0 && tempSigJ.Length > 0
                                && !string.Equals(tempSigI, tempSigJ, StringComparison.OrdinalIgnoreCase))
                            {
                                findings.Add(new ValidationFinding
                                {
                                    Id = Guid.NewGuid(),
                                    RunId = runId,
                                    ClaimId = groupList[i].Id,
                                    ValidatorName = nameof(NumericContradictionValidator),
                                    Status = ValidationStatus.Unverified,
                                    Message = $"Different temperature regimes ({tempSigI} vs {tempSigJ}): {groupList[i].RawText} / {groupList[j].RawText}; not contradictory.",
                                    Confidence = 0.8,
                                    EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                    Kind = FindingKind.DifferentConditionContext,
                                    Category = FindingCategory.Diagnostic
                                });
                                continue;
                            }
                        }

                        // Check chromatography gradient for percent claims (skip yield/purity/impurity � those are never gradients)
                        if (groupList[i].Unit == "%"
                            && groupContextKey is not "yield" and not "purity" and not "impurity"
                            && IsChromatographyGradient(run.GetAnalyzedText(), groupList[i], groupList[j]))
                        {
                            findings.Add(new ValidationFinding
                            {
                                Id = Guid.NewGuid(),
                                RunId = runId,
                                ClaimId = groupList[i].Id,
                                ValidatorName = nameof(NumericContradictionValidator),
                                Status = ValidationStatus.Pass,
                                Message = $"Chromatography gradient detected ({groupList[i].RawText} ? {groupList[j].RawText}); not contradictory.",
                                Confidence = 0.9,
                                EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                Kind = FindingKind.GradientElution,
                                Category = FindingCategory.Diagnostic
                            });
                            continue;
                        }

                        // Emit a single canonical finding per pair (i < j ensures no duplication)
                        findings.Add(new ValidationFinding
                        {
                            Id = Guid.NewGuid(),
                            RunId = runId,
                            ClaimId = groupList[i].Id,
                            ValidatorName = nameof(NumericContradictionValidator),
                            Status = ValidationStatus.Fail,
                            Message = $"Possible contradiction: {groupList[i].RawText} vs {groupList[j].RawText}.",
                            Confidence = 0.7,
                            EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                            Kind = FindingKind.Contradiction
                        });
                    }
                    else
                    {
                        bool isConsistent = average == 0
                            ? valA == valB
                            : (diff / average) * 100.0 <= ConsistencyThresholdPercent;

                        if (isConsistent)
                        {
                            findings.Add(new ValidationFinding
                            {
                                Id = Guid.NewGuid(),
                                RunId = runId,
                                ClaimId = groupList[i].Id,
                                ValidatorName = nameof(NumericContradictionValidator),
                                Status = ValidationStatus.Pass,
                                Message = $"Claims are consistent: {groupList[i].RawText} \u2248 {groupList[j].RawText} (equivalent after unit normalization).",
                                Confidence = 0.95,
                                EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}"
                            });
                        }
                        else
                        {
                            findings.Add(new ValidationFinding
                            {
                                Id = Guid.NewGuid(),
                                RunId = runId,
                                ClaimId = groupList[i].Id,
                                ValidatorName = nameof(NumericContradictionValidator),
                                Status = ValidationStatus.Pass,
                                Message = $"No contradiction detected between {groupList[i].RawText} and {groupList[j].RawText}.",
                                Confidence = 0.8,
                                EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                Category = FindingCategory.Diagnostic
                            });
                        }
                    }
                }

                if (emittedGroupLevel) break;
            }

            // Cross-step/cross-cluster condition differences ? check for multi-scenario language first
            if (crossScopeConflictFound && !emittedGroupLevel)
            {
                string claimRefs = string.Join("+", groupList.Select(c => $"Claim:{c.Id}"));
                string rawValues = string.Join(", ", groupList.Select(c => c.RawText));
                string analyzedText = run.GetAnalyzedText();

                bool multiScenario = groupList.Any(c => HasMultiScenarioContext(analyzedText, c));

                if (multiScenario)
                {
                    findings.Add(new ValidationFinding
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        ClaimId = null,
                        ValidatorName = nameof(NumericContradictionValidator),
                        Status = ValidationStatus.Unverified,
                        Message = $"Multiple scenarios detected ({rawValues}); values may refer to different conditions.",
                        Confidence = 0.5,
                        EvidenceRef = claimRefs,
                        Kind = FindingKind.MultiScenario
                    });
                }
                else
                {
                    findings.Add(new ValidationFinding
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        ClaimId = null,
                        ValidatorName = nameof(NumericContradictionValidator),
                        Status = ValidationStatus.Unverified,
                        Message = $"Multiple distinct {groupContextKey} values observed across steps ({rawValues}); expected for multistep synthesis.",
                        Confidence = 0.4,
                        EvidenceRef = claimRefs,
                        Kind = FindingKind.CrossStepConditionVariation,
                        Category = FindingCategory.Diagnostic
                    });
                }
            }
        }

        return findings;
    }

    private static string ExtractContextKey(ExtractedClaim claim)
    {
        if (claim.JsonPayload is null)
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(claim.JsonPayload);
            if (doc.RootElement.TryGetProperty("contextKey", out JsonElement ck))
            {
                return ck.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Malformed payload
        }

        return string.Empty;
    }

    private static string ExtractTimeAction(ExtractedClaim claim)
    {
        if (claim.JsonPayload is null)
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(claim.JsonPayload);
            if (doc.RootElement.TryGetProperty("timeAction", out JsonElement ta))
            {
                return ta.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Malformed payload
        }

        return string.Empty;
    }

    private static string BuildGroupKey(ExtractedClaim claim)
    {
        string contextKey = ExtractContextKey(claim);
        string canonicalUnit = UnitNormalizer.GetCanonicalUnit(claim.Unit ?? string.Empty);

        return $"{contextKey}|{canonicalUnit}";
    }

    /// <summary>
    /// Returns true if two claims are in the same step or the same condition cluster.
    /// </summary>
    private static bool AreInSameScope(ExtractedClaim a, ExtractedClaim b,
        IReadOnlyDictionary<int, string> clusterMap)
    {
        int stepA = a.StepIndex ?? -1;
        int stepB = b.StepIndex ?? -1;

        // Same step (including both step-less ? both -1)
        if (stepA == stepB) return true;

        // Same cluster label
        if (stepA >= 0 && stepB >= 0
            && clusterMap.TryGetValue(stepA, out string? labelA)
            && clusterMap.TryGetValue(stepB, out string? labelB)
            && string.Equals(labelA, labelB, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if two claims have distinct, non-null entity keys
    /// while belonging to the same step (or are both step-less).
    /// This prevents comparing "benzaldehyde 1.06 g" with "NaBH4 0.38 g".
    /// </summary>
    private static bool AreDifferentEntitiesInSameStep(ExtractedClaim a, ExtractedClaim b)
    {
        // If either claim lacks an entity key, we cannot scope � allow comparison
        if (a.EntityKey is null || b.EntityKey is null)
        {
            return false;
        }

        // Different entity keys ? different reagents
        if (!string.Equals(a.EntityKey, b.EntityKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if two time claims refer to demonstrably different step actions
    /// (e.g., "addition" vs "stir"), meaning they should not be compared.
    /// Returns false if either has no timeAction (ambiguous = still comparable).
    /// </summary>
    private static bool HasDifferentTimeActions(ExtractedClaim a, ExtractedClaim b)
    {
        string actionA = ExtractTimeAction(a);
        string actionB = ExtractTimeAction(b);

        if (actionA.Length == 0 || actionB.Length == 0)
        {
            return false;
        }

        return !string.Equals(actionA, actionB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether strong multi-scenario language (e.g., "alternative", "route", "variant")
    /// appears within a window around the claim's source location in the output text.
    /// </summary>
    private static bool HasMultiScenarioContext(string output, ExtractedClaim claim)
    {
        const int windowChars = 80;

        if (claim.SourceLocator is null || !claim.SourceLocator.StartsWith("AnalyzedText:"))
        {
            // Fallback: scan the entire output
            return MultiScenarioRegex.IsMatch(output);
        }

        string span = claim.SourceLocator["AnalyzedText:".Length..];
        string[] parts = span.Split('-');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], out int start) ||
            !int.TryParse(parts[1], out int end))
        {
            return MultiScenarioRegex.IsMatch(output);
        }

        int windowStart = Math.Max(0, start - windowChars);
        int windowEnd = Math.Min(output.Length, end + windowChars);
        string window = output[windowStart..windowEnd];

        return MultiScenarioRegex.IsMatch(window);
    }

    /// <summary>
    /// Detects whether two time claims represent sequential durations rather than
    /// contradictory values (e.g., "reflux for 30 min � an additional 15 min").
    /// Returns the matched additive cue phrase, or null if not sequential.
    /// </summary>
    public static string? DetectSequentialDuration(string text, ExtractedClaim claimA, ExtractedClaim claimB)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // Determine positional order from SourceLocator
        if (!EvidenceLocator.TryParse(claimA.SourceLocator, out int startA, out int endA) ||
            !EvidenceLocator.TryParse(claimB.SourceLocator, out int startB, out int endB))
        {
            return null;
        }

        // Ensure A comes before B
        if (startA > startB)
        {
            (startA, endA, startB, endB) = (startB, endB, startA, endA);
        }

        // Check for an additive cue in a window before the later claim (up to 60 chars)
        const int cueWindow = 60;
        int searchStart = Math.Max(endA, startB - cueWindow);
        int searchEnd = Math.Min(text.Length, endB);
        if (searchStart >= searchEnd) return null;

        string betweenText = text[searchStart..searchEnd];
        Match cueMatch = AdditiveCueRegex.Match(betweenText);
        if (!cueMatch.Success) return null;

        // Verify both claims share a common operation anchor nearby
        int opWindowA = Math.Max(0, startA - 60);
        string contextA = text[opWindowA..Math.Min(text.Length, endA + 10)];
        int opWindowB = Math.Max(0, startB - 60);
        string contextB = text[opWindowB..Math.Min(text.Length, endB + 10)];

        bool opA = TimeOperationRegex.IsMatch(contextA);
        bool opB = TimeOperationRegex.IsMatch(contextB);

        // Accept if at least the first claim has an operation anchor (the second
        // often just says "an additional 15 min" without repeating the verb)
        if (!opA && !opB) return null;

        return cueMatch.Value;
    }

    /// <summary>
    /// Infers an operation tag for a time claim by finding the closest operation
    /// verb to the claim's source location. Uses proximity ranking so that the
    /// nearest cue word wins, avoiding false matches from adjacent clauses.
    /// Returns an empty string when no operation can be inferred.
    /// </summary>
    internal static string InferOperationTag(string text, ExtractedClaim claim)
    {
        if (!EvidenceLocator.TryParse(claim.SourceLocator, out int start, out int end))
        {
            return string.Empty;
        }

        const int backwardChars = 60;
        const int forwardChars = 15;
        int windowStart = Math.Max(0, start - backwardChars);
        int windowEnd = Math.Min(text.Length, end + forwardChars);
        string window = text[windowStart..windowEnd];
        int claimOffset = start - windowStart;

        string bestTag = string.Empty;
        int bestDistance = int.MaxValue;

        FindClosestMatch(OpAddDoseRegex, window, claimOffset, "AddDose", ref bestTag, ref bestDistance);
        FindClosestMatch(OpStirHoldRegex, window, claimOffset, "StirHold", ref bestTag, ref bestDistance);
        FindClosestMatch(OpHeatRefluxRegex, window, claimOffset, "HeatReflux", ref bestTag, ref bestDistance);
        FindClosestMatch(OpCoolQuenchRegex, window, claimOffset, "CoolQuench", ref bestTag, ref bestDistance);
        FindClosestMatch(OpWaitStandRegex, window, claimOffset, "WaitStand", ref bestTag, ref bestDistance);

        return bestTag;
    }

    private static void FindClosestMatch(
        Regex regex, string window, int claimOffset, string tag,
        ref string bestTag, ref int bestDistance)
    {
        foreach (Match m in regex.Matches(window))
        {
            int distance = Math.Min(
                Math.Abs(claimOffset - m.Index),
                Math.Abs(claimOffset - (m.Index + m.Length)));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTag = tag;
            }
        }
    }

    /// <summary>
    /// Returns true when one time claim is introduced by a checkpoint cue ("after", "upon", �)
    /// and the other by a cumulative cue ("total", "for a total of", �).
    /// </summary>
    internal static bool IsCheckpointVsTotal(string text, ExtractedClaim a, ExtractedClaim b)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (!EvidenceLocator.TryParse(a.SourceLocator, out int startA, out int endA) ||
            !EvidenceLocator.TryParse(b.SourceLocator, out int startB, out int endB))
        {
            return false;
        }

        const int cueWindow = 40;

        string windowA = text[Math.Max(0, startA - cueWindow)..Math.Min(text.Length, endA + 10)];
        string windowB = text[Math.Max(0, startB - cueWindow)..Math.Min(text.Length, endB + 10)];

        bool checkpointA = CheckpointCueRegex.IsMatch(windowA);
        bool checkpointB = CheckpointCueRegex.IsMatch(windowB);
        bool cumulativeA = CumulativeCueRegex.IsMatch(windowA);
        bool cumulativeB = CumulativeCueRegex.IsMatch(windowB);

        return (checkpointA && cumulativeB) || (checkpointB && cumulativeA);
    }

    /// <summary>
    /// Extracts a normalised temperature signature from the text near a time claim
    /// (�80 chars). Returns the closest match normalised to a category string
    /// (e.g. "0C", "ambient", "reflux"). Empty string means no temperature found.
    /// </summary>
    internal static string ExtractNearbyTempSignature(string text, ExtractedClaim claim)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        if (!EvidenceLocator.TryParse(claim.SourceLocator, out int start, out int end))
        {
            return string.Empty;
        }

        const int windowChars = 80;
        int windowStart = Math.Max(0, start - windowChars);
        int windowEnd = Math.Min(text.Length, end + windowChars);
        string window = text[windowStart..windowEnd];
        int claimOffset = start - windowStart;

        string bestSig = string.Empty;
        int bestDistance = int.MaxValue;

        foreach (Match m in ConditionTempRegex.Matches(window))
        {
            int distance = Math.Min(
                Math.Abs(claimOffset - m.Index),
                Math.Abs(claimOffset - (m.Index + m.Length)));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSig = NormalizeTempSignature(m.Value);
            }
        }

        return bestSig;
    }

    private static string NormalizeTempSignature(string raw)
    {
        string lower = raw.Trim().ToLowerInvariant();

        if (lower.Contains("room") || lower.Contains("ambient") || lower is "rt" or "r.t." or "rt.")
            return "ambient";
        if (lower.Contains("reflux"))
            return "reflux";
        if (lower.Contains("ice"))
            return "ice";

        // Extract numeric part for explicit temperatures
        Match numMatch = Regex.Match(lower, @"-?\d+(?:\.\d+)?");
        return numMatch.Success ? numMatch.Value + "C" : lower;
    }

    /// <summary>
    /// Returns true if both percent claims are within a chromatography gradient context
    /// (keywords within �120 chars of each claim).
    /// </summary>
    internal static bool IsChromatographyGradient(string text, ExtractedClaim a, ExtractedClaim b)
    {
        return HasChromatographyContext(text, a) && HasChromatographyContext(text, b);
    }

    private static bool HasChromatographyContext(string text, ExtractedClaim claim)
    {
        if (!EvidenceLocator.TryParse(claim.SourceLocator, out int start, out int end))
        {
            return false;
        }

        const int windowChars = 120;
        int windowStart = Math.Max(0, start - windowChars);
        int windowEnd = Math.Min(text.Length, end + windowChars);
        string window = text[windowStart..windowEnd];

        return ChromatographyContextRegex.IsMatch(window);
    }
}

