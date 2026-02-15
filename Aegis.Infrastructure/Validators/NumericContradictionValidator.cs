using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aegis.Core;
using Aegis.Core.Enums;
using Aegis.Core.Interfaces;
using Aegis.Core.Models;
using Aegis.Infrastructure.Services;

namespace Aegis.Infrastructure.Validators;

public class NumericContradictionValidator : IValidator
{
    private const double ContradictionThresholdPercent = 50.0;
    private const double ConsistencyThresholdPercent = 5.0;

    private static readonly Regex MultiScenarioRegex = new(
        @"\b(alternativ\w*|route|separate\w*|trial|condition\s*set|variant|respective\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();

        List<ExtractedClaim> numericClaims = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit && c.Unit is not null)
            .ToList();

        // Group by (ContextKey + Unit) for more precise contradiction detection
        IEnumerable<IGrouping<string, ExtractedClaim>> groupedByUnit = numericClaims
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
                        bool multiScenario = HasMultiScenarioContext(run.Output, groupList[i]) ||
                                             HasMultiScenarioContext(run.Output, groupList[j]);

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

                        findings.Add(new ValidationFinding
                        {
                            Id = Guid.NewGuid(),
                            RunId = runId,
                            ClaimId = groupList[j].Id,
                            ValidatorName = nameof(NumericContradictionValidator),
                            Status = ValidationStatus.Fail,
                            Message = $"Possible contradiction: {groupList[j].RawText} vs {groupList[i].RawText}.",
                            Confidence = 0.7,
                            EvidenceRef = $"Claim:{groupList[j].Id}+Claim:{groupList[i].Id}",
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

    private static string BuildGroupKey(ExtractedClaim claim)
    {
        string contextKey = string.Empty;

        if (claim.JsonPayload is not null)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(claim.JsonPayload);
                if (doc.RootElement.TryGetProperty("contextKey", out JsonElement ck))
                {
                    contextKey = ck.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // Malformed payload — fall through to unit-only grouping
            }
        }

        string canonicalUnit = UnitNormalizer.GetCanonicalUnit(claim.Unit ?? string.Empty);
        return $"{contextKey}|{canonicalUnit}";
    }

    /// <summary>
    /// Checks whether strong multi-scenario language (e.g., "alternative", "route", "variant")
    /// appears within a window around the claim's source location in the output text.
    /// </summary>
    private static bool HasMultiScenarioContext(string output, ExtractedClaim claim)
    {
        const int windowChars = 80;

        if (claim.SourceLocator is null || !claim.SourceLocator.StartsWith("Output:"))
        {
            // Fallback: scan the entire output
            return MultiScenarioRegex.IsMatch(output);
        }

        string span = claim.SourceLocator["Output:".Length..];
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
