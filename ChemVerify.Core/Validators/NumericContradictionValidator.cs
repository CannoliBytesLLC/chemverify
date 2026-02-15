using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Core.Validators;

public class NumericContradictionValidator : IValidator
{
    private const double ContradictionThresholdPercent = 50.0;
    private const double ConsistencyThresholdPercent = 5.0;

    private static readonly Regex MultiScenarioRegex = new(
        @"\b(alternativ\w*|route|separate\w*|trial|condition\s*set|variant|respective\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ComparableContextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "temp", "time", "yield", "conc"
    };

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();

        List<ExtractedClaim> numericClaims = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit && c.Unit is not null)
            .ToList();

        // Partition: only claims with a comparable contextKey enter contradiction logic
        List<ExtractedClaim> comparableClaims = new();

        foreach (ExtractedClaim claim in numericClaims)
        {
            string contextKey = ExtractContextKey(claim);

            if (ComparableContextKeys.Contains(contextKey))
            {
                comparableClaims.Add(claim);
            }
            else
            {
                findings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ClaimId = claim.Id,
                    ValidatorName = nameof(NumericContradictionValidator),
                    Status = ValidationStatus.Unverified,
                    Message = $"Numeric claim ({claim.RawText}) has no comparable context; skipped for contradiction checking.",
                    Confidence = 0.3,
                    EvidenceRef = $"Claim:{claim.Id}",
                    Kind = FindingKind.NotComparable
                });
            }
        }

        // Group comparable claims by (ContextKey + Unit + EntityKey) for step-scoped comparison
        IEnumerable<IGrouping<string, ExtractedClaim>> groupedByUnit = comparableClaims
            .GroupBy(c => BuildGroupKey(c));

        foreach (IGrouping<string, ExtractedClaim> group in groupedByUnit)
        {
            List<ExtractedClaim> groupList = group.ToList();

            if (groupList.Count < 2)
            {
                foreach (ExtractedClaim claim in groupList)
                {
                    findings.Add(new ValidationFinding
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        ClaimId = claim.Id,
                        ValidatorName = nameof(NumericContradictionValidator),
                        Status = ValidationStatus.Unverified,
                        Message = "Single numeric claim for this context+unit; cannot check for contradictions.",
                        Confidence = 0.5,
                        EvidenceRef = $"Claim:{claim.Id}",
                        Kind = FindingKind.NotCheckable
                    });
                }
                continue;
            }

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

                    if (isContradiction)
                    {
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

                            // Skip remaining pairs in this group — already handled at group level
                            goto NextGroup;
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
                                EvidenceRef = $"Claim:{groupList[i].Id}+Claim:{groupList[j].Id}"
                            });
                        }
                    }
                }
            }

            NextGroup:;
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
    /// Returns true if two claims have distinct, non-null entity keys
    /// while belonging to the same step (or are both step-less).
    /// This prevents comparing "benzaldehyde 1.06 g" with "NaBH4 0.38 g".
    /// </summary>
    private static bool AreDifferentEntitiesInSameStep(ExtractedClaim a, ExtractedClaim b)
    {
        // If either claim lacks an entity key, we cannot scope — allow comparison
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
}

