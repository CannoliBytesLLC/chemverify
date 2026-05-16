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
        @"\b(an?\s+additional|additional|another|for\s+another|then|followed\s+by|and\s+then|after\s+which|after|subsequently|over|next|thereafter)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Generalized sequential-operation cues. Used as an early gate for ANY contextKey
    // (mass, yield, conc, time, ...) to suppress cross-operation contradictions.
    private static readonly Regex SequentialOperationCueRegex = new(
        @"\b(then|after(?:\s+which)?|followed\s+by|subsequently|thereafter|next|"
        + @"an?\s+additional|additional|another|for\s+another|more|"
        + @"before|over|allowed\s+to|warmed\s+to|cooled\s+to|"
        + @"stirred\s+for|heated\s+for|refluxed\s+for|aged\s+for|"
        + @"filtered\s+then|washed\s+then|dried\s+then|"
        + @"first\s+crop|second\s+crop|crude\s+product|purified\s+product|"
        + @"and\s+then)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Analytical-context cues. When present near BOTH numeric claims, the values
    // are part of analytical reporting (NMR multiplicity counts, MS m/z, elemental
    // analysis percentages, HPLC purity, Rf, optical rotation) and must not be
    // compared as reaction-condition contradictions.
    private static readonly Regex AnalyticalContextRegex = new(
        @"(?<![A-Za-z])(?:"
        + @"\d{1,3}\s*[CHNPF]\s*NMR"                       // 1H NMR, 13C NMR, 31P NMR
        + @"|NMR|HRMS|LRMS|LCMS|GCMS|HPLC|UPLC|TLC|"
        + @"m/z|M\+1|\[M\+|\[M\-|"
        + @"Anal(?:ysis)?\.?\s*(?:calc(?:ulated|d)?|found)|"
        + @"Found\s*[:;]\s*[CHNOPS]|"
        + @"Calc(?:ulated|d)?\.?\s*(?:for|[:;])|"
        + @"\bRf\b|"
        + @"\[\u03B1\]|optical\s+rotation|"
        + @"\b\d+\s*MHz\b|\bppm\b|"
        + @"multiplet|singlet|doublet|triplet|quartet|"
        + @"\bdd\b|\bdt\b|\btd\b|\bdq\b|"
        + @"\bJ\s*=|"
        + @"integration"
        + @")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bracketed integration patterns common to NMR reports: "(1H, dd, J = ...)",
    // "(2H, m)", "1H," etc. Catches cases where the bare numeric pulled by the
    // extractor was actually an NMR proton count.
    private static readonly Regex NmrIntegrationFragmentRegex = new(
        @"\(\s*\d+\s*H\s*[,)]"
        + @"|\d+\s*H\s*,\s*(?:s|d|t|q|m|br|dd|dt|td|dq|qd|ddd|tt)\b"
        + @"|\d+\s*H\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Sets used by the cross-step entity gate. These context keys describe a
    // "thing" (yield of compound X, concentration of solution Y, mass of crop Z)
    // rather than a global reaction condition (temp/time).
    private static readonly HashSet<string> EntityScopedContextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "yield", "purity", "impurity", "conc"
    };

    // Carveout: phrases like "overall yield of the process" / "total yield of
    // the synthesis" are aggregate yield claims that legitimately contradict a
    // per-step yield. The universal sequential/entity gates must NOT suppress
    // these pairs.
    private static readonly Regex OverallYieldRegex = new(
        @"\b(?:overall|total|cumulative|combined|aggregate)\s+yield"
        + @"|\byield\s+of\s+the\s+(?:process|synthesis|sequence|overall|reaction)\b",
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

                        // ── Universal gate 1: Analytical-notation suppression ────────
                        // Pairs sitting in NMR/MS/HPLC/Anal/Rf context are not
                        // reaction-condition contradictions.
                        if (IsAnalyticalNotation(run.GetAnalyzedText(), groupList[i], groupList[j]))
                        {
                            findings.Add(new ValidationFinding
                            {
                                Id = Guid.NewGuid(),
                                RunId = runId,
                                ClaimId = groupList[i].Id,
                                ValidatorName = nameof(NumericContradictionValidator),
                                Status = ValidationStatus.Pass,
                                Message = $"Analytical notation context ({groupList[i].RawText} / {groupList[j].RawText}); not a reaction-condition contradiction.",
                                Confidence = 0.9,
                                EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                Kind = FindingKind.AnalyticalNotationIgnored,
                                Category = FindingCategory.Diagnostic
                            });
                            continue;
                        }

                        // ── Universal gate 2: Sequential-operation cue ───────────────
                        // Generalizes the time-only sequential-duration rule to all
                        // context keys (mass, yield, conc, ...). If a sequential
                        // operator sits between the two claims, treat them as
                        // describing distinct operations.
                        // Carveout: "overall/total yield" claims are aggregate
                        // assertions that legitimately contradict per-step yields.
                        bool aggregateYieldPair =
                            string.Equals(groupContextKey, "yield", StringComparison.OrdinalIgnoreCase)
                            && OverallYieldRegex.IsMatch(run.GetAnalyzedText());

                        if (!aggregateYieldPair
                            && HasSequentialOperationCueBetween(run.GetAnalyzedText(), groupList[i], groupList[j]))
                        {
                            findings.Add(new ValidationFinding
                            {
                                Id = Guid.NewGuid(),
                                RunId = runId,
                                ClaimId = groupList[i].Id,
                                ValidatorName = nameof(NumericContradictionValidator),
                                Status = ValidationStatus.Pass,
                                Message = $"Sequential-operation variation ({groupList[i].RawText} → {groupList[j].RawText}); values describe distinct operations.",
                                Confidence = 0.85,
                                EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                Kind = FindingKind.SequentialOperationVariation,
                                Category = FindingCategory.Diagnostic
                            });
                            continue;
                        }

                        // ── Universal gate 3: Cross-step distinct-entity ─────────────
                        // For entity-scoped groups (yield/purity/impurity/conc), if
                        // the two claims are in different steps and either has an
                        // entity key (and the keys differ, or one is unknown), they
                        // describe different things — not contradictory.
                        // Same overall/total-yield carveout applies.
                        if (!aggregateYieldPair
                            && EntityScopedContextKeys.Contains(groupContextKey)
                            && AreDifferentEntitiesAcrossSteps(groupList[i], groupList[j]))
                        {
                            findings.Add(new ValidationFinding
                            {
                                Id = Guid.NewGuid(),
                                RunId = runId,
                                ClaimId = groupList[i].Id,
                                ValidatorName = nameof(NumericContradictionValidator),
                                Status = ValidationStatus.Pass,
                                Message = $"Different entity context ({groupList[i].RawText} vs {groupList[j].RawText}); values describe distinct items.",
                                Confidence = 0.8,
                                EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}",
                                Kind = FindingKind.DifferentEntityVariation,
                                Category = FindingCategory.Diagnostic
                            });
                            continue;
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
    /// Returns true when two claims are in different steps and at least one
    /// piece of evidence indicates they describe different entities:
    ///   - both have entity keys and they differ, OR
    ///   - the step indices differ and entity keys are not both populated as the same value.
    /// Used for entity-scoped groups (yield/purity/impurity/conc) where a cross-step
    /// pair almost always describes different intermediates/products/solutions.
    /// </summary>
    private static bool AreDifferentEntitiesAcrossSteps(ExtractedClaim a, ExtractedClaim b)
    {
        int stepA = a.StepIndex ?? -1;
        int stepB = b.StepIndex ?? -1;

        // Same step ? not a cross-step pair (handled by other gates)
        if (stepA >= 0 && stepB >= 0 && stepA == stepB) return false;

        // Both have entity keys: different keys ? different entities.
        if (a.EntityKey is not null && b.EntityKey is not null)
        {
            return !string.Equals(a.EntityKey, b.EntityKey, StringComparison.OrdinalIgnoreCase);
        }

        // At least one has a step index and they differ ? treat as different entity context.
        if (stepA >= 0 && stepB >= 0 && stepA != stepB) return true;

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

    /// <summary>
    /// Returns true when both numeric claims sit in analytical-reporting context
    /// (NMR multiplicity, MS m/z, elemental analysis, HPLC purity, Rf, optical rotation).
    /// Such values must not be compared as reaction-condition contradictions.
    /// A single-claim NMR-integration fragment is also accepted because the partner
    /// claim is then almost always an unrelated reaction value miscompared against it.
    /// </summary>
    public static bool IsAnalyticalNotation(string text, ExtractedClaim a, ExtractedClaim b)
    {
        if (string.IsNullOrEmpty(text)) return false;

        bool aAnalytical = HasAnalyticalContext(text, a);
        bool bAnalytical = HasAnalyticalContext(text, b);

        if (aAnalytical && bAnalytical) return true;

        // If one side is clearly an NMR integration fragment, treat the pair as analytical.
        if (aAnalytical || bAnalytical)
        {
            return IsNmrIntegrationFragment(text, a) || IsNmrIntegrationFragment(text, b);
        }

        return false;
    }

    private static bool HasAnalyticalContext(string text, ExtractedClaim claim)
    {
        if (!EvidenceLocator.TryParse(claim.SourceLocator, out int start, out int end))
        {
            return false;
        }

        const int windowChars = 100;
        int windowStart = Math.Max(0, start - windowChars);
        int windowEnd = Math.Min(text.Length, end + windowChars);
        string window = text[windowStart..windowEnd];

        return AnalyticalContextRegex.IsMatch(window);
    }

    private static bool IsNmrIntegrationFragment(string text, ExtractedClaim claim)
    {
        if (!EvidenceLocator.TryParse(claim.SourceLocator, out int start, out int end))
        {
            return false;
        }

        const int windowChars = 30;
        int windowStart = Math.Max(0, start - windowChars);
        int windowEnd = Math.Min(text.Length, end + windowChars);
        string window = text[windowStart..windowEnd];

        return NmrIntegrationFragmentRegex.IsMatch(window);
    }

    /// <summary>
    /// Returns true when a sequential-operation cue word appears in the text
    /// strictly between the two claim positions. This generalizes the time-only
    /// <see cref="DetectSequentialDuration"/> rule to any context key.
    /// </summary>
    public static bool HasSequentialOperationCueBetween(string text, ExtractedClaim a, ExtractedClaim b)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (!EvidenceLocator.TryParse(a.SourceLocator, out int startA, out int endA) ||
            !EvidenceLocator.TryParse(b.SourceLocator, out int startB, out int endB))
        {
            return false;
        }

        if (startA > startB)
        {
            (startA, endA, startB, endB) = (startB, endB, startA, endA);
        }

        // Only a true between-region counts; no large overshoot.
        int searchStart = Math.Max(0, endA);
        int searchEnd = Math.Min(text.Length, startB);
        if (searchStart >= searchEnd) return false;

        // Cap the gap; cross-paragraph cues are not informative.
        const int maxGap = 220;
        if (searchEnd - searchStart > maxGap) return false;

        string between = text[searchStart..searchEnd];
        return SequentialOperationCueRegex.IsMatch(between);
    }
}

