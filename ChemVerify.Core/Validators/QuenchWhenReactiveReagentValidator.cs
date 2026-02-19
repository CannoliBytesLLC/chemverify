using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;

namespace ChemVerify.Core.Validators;

/// <summary>
/// If a reactive reagent (NaH, LAH, Grignard, BuLi, etc.) is present
/// and no quench/workup phrase appears in a later step, emit a
/// <see cref="FindingKind.MissingQuench"/> finding.
/// </summary>
public class QuenchWhenReactiveReagentValidator : IValidator
{
    private static readonly HashSet<string> ReactiveRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "reductant", "base", "organometallic"
    };

    private static readonly Regex QuenchWorkupRegex = new(
        @"\b(quench(?:ed|ing)?|work[- ]?up|workup|extract(?:ed|ion)|wash(?:ed|ing)?|"
        + @"pour(?:ed)?\s+(?:into|onto)|added?\s+(?:to\s+)?(?:ice|water|sat\w*\s+NH4Cl|"
        + @"sat\w*\s+NaHCO3|brine)|neutrali[sz](?:ed|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();
        if (string.IsNullOrEmpty(text)) return findings;

        // Find all reactive reagent claims
        List<ExtractedClaim> reactives = claims
            .Where(c => c.ClaimType == ClaimType.ReagentMention
                     && c.JsonPayload is not null
                     && ReactiveRoles.Any(r => c.JsonPayload.Contains($"\"role\":\"{r}\"", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (reactives.Count == 0) return findings;

        int maxReactiveStep = reactives.Max(c => c.StepIndex ?? 0);

        // Check if any quench/workup language exists in steps after the last reactive reagent
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        bool hasQuench = false;

        foreach (TextStep step in steps)
        {
            if (step.Index <= maxReactiveStep) continue;

            string stepText = text[step.StartOffset..step.EndOffset];
            if (QuenchWorkupRegex.IsMatch(stepText))
            {
                hasQuench = true;
                break;
            }
        }

        // Also check if quench language appears in the same step as the reactive (e.g. "NaBH4 was added ... then quenched")
        if (!hasQuench)
        {
            foreach (TextStep step in steps)
            {
                if (step.Index != maxReactiveStep) continue;
                string stepText = text[step.StartOffset..step.EndOffset];
                // Only count if the quench phrase comes AFTER the reactive token in the same step
                ExtractedClaim lastReactive = reactives.Last(c => (c.StepIndex ?? 0) == maxReactiveStep);
                int reactiveEnd = 0;
                if (lastReactive.SourceLocator is not null && lastReactive.SourceLocator.StartsWith("AnalyzedText:"))
                {
                    string[] parts = lastReactive.SourceLocator["AnalyzedText:".Length..].Split('-');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int end))
                        reactiveEnd = end;
                }
                if (reactiveEnd > step.StartOffset)
                {
                    string afterReactive = text[reactiveEnd..step.EndOffset];
                    if (QuenchWorkupRegex.IsMatch(afterReactive))
                        hasQuench = true;
                }
                break;
            }
        }

        if (!hasQuench)
        {
            string reagentTokens = string.Join(", ", reactives.Select(c => c.RawText).Distinct());
            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = nameof(QuenchWhenReactiveReagentValidator),
                Status = ValidationStatus.Fail,
                Message = $"[CHEM.MISSING_QUENCH] Reactive reagent ({reagentTokens}) detected but no quench/workup step found.",
                Confidence = 0.85,
                Kind = FindingKind.MissingQuench,
                EvidenceRef = $"Reagent:{reagentTokens}|LastStep:{maxReactiveStep}",
                JsonPayload = $"{{\"reagents\":\"{EscapeJson(reagentTokens)}\",\"lastReactiveStep\":{maxReactiveStep}}}"
            });
        }

        return findings;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
