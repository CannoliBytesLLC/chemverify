using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;
using ChemVerify.Core.Validation;

namespace ChemVerify.Core.Validators;

/// <summary>
/// If a reactive reagent (NaH, LAH, Grignard, BuLi, etc.) is present
/// and no quench/workup phrase appears in a later step, emit a
/// <see cref="FindingKind.MissingQuench"/> finding.
/// Skips non-procedural text to avoid false positives on narrative writing.
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

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);

        // Gate: skip non-procedural text to avoid false positives on narrative/review writing
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);
        if (!ctx.IsProcedural) return findings;

        // Classify step roles to filter out questions/narrative
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(text, steps, ctx.ReferencesStartOffset);

        // Determine the effective text boundary (ignore references section)
        int textBoundary = ctx.ReferencesStartOffset ?? text.Length;

        // Find all reactive reagent claims that appear before the references section
        // and belong to steps classified as Procedure
        List<ExtractedClaim> reactives = claims
            .Where(c => c.ClaimType == ClaimType.ReagentMention
                     && c.JsonPayload is not null
                     && ReactiveRoles.Any(r => c.JsonPayload.Contains($"\"role\":\"{r}\"", StringComparison.OrdinalIgnoreCase))
                     && IsBeforeBoundary(c, textBoundary)
                     && IsInProceduralStep(c, roles))
            .ToList();

        if (reactives.Count == 0) return findings;

        int maxReactiveStep = reactives.Max(c => c.StepIndex ?? 0);

        // Check if any quench/workup language exists in steps after the last reactive reagent
        // (only within the pre-references portion of the text)
        bool hasQuench = false;

        foreach (TextStep step in steps)
        {
            if (step.Index <= maxReactiveStep) continue;
            if (step.StartOffset >= textBoundary) break;

            int endOffset = Math.Min(step.EndOffset, textBoundary);
            string stepText = text[step.StartOffset..endOffset];
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
                if (step.StartOffset >= textBoundary) break;

                // Only count if the quench phrase comes AFTER the reactive token in the same step
                ExtractedClaim lastReactive = reactives.Last(c => (c.StepIndex ?? 0) == maxReactiveStep);
                int reactiveEnd = 0;
                if (EvidenceLocator.TryParse(lastReactive.SourceLocator, out _, out int end))
                    reactiveEnd = end;

                if (reactiveEnd > step.StartOffset)
                {
                    int endOffset = Math.Min(step.EndOffset, textBoundary);
                    string afterReactive = text[reactiveEnd..endOffset];
                    if (QuenchWorkupRegex.IsMatch(afterReactive))
                        hasQuench = true;
                }
                break;
            }
        }

        if (!hasQuench)
        {
            string reagentTokens = string.Join(", ", reactives.Select(c => c.RawText).Distinct());

            // Attach evidence from the last reactive reagent claim
            ExtractedClaim evidenceClaim = reactives[^1];
            int? evidenceStart = null;
            int? evidenceEnd = null;
            string? evidenceSnippet = null;

            if (EvidenceLocator.TryParse(evidenceClaim.SourceLocator, out int es, out int ee))
            {
                evidenceStart = es;
                evidenceEnd = ee;
                evidenceSnippet = EvidenceLocator.ExtractSnippet(text, es, ee);
            }

            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ValidatorName = nameof(QuenchWhenReactiveReagentValidator),
                Status = ValidationStatus.Fail,
                Message = $"[CHEM.MISSING_QUENCH] Reactive reagent ({reagentTokens}) detected but no quench/workup step found.",
                Confidence = 0.85,
                Kind = FindingKind.MissingQuench,
                EvidenceRef = evidenceClaim.SourceLocator ?? $"Reagent:{reagentTokens}|LastStep:{maxReactiveStep}",
                EvidenceStartOffset = evidenceStart,
                EvidenceEndOffset = evidenceEnd,
                EvidenceStepIndex = evidenceClaim.StepIndex,
                EvidenceEntityKey = evidenceClaim.EntityKey,
                EvidenceSnippet = evidenceSnippet,
                JsonPayload = $"{{\"reagents\":\"{EscapeJson(reagentTokens)}\",\"lastReactiveStep\":{maxReactiveStep}}}"
            });
        }

        return findings;
    }

    /// <summary>
    /// Returns true if the claim's source locator offset is before the boundary
    /// (i.e. not inside the references section).
    /// </summary>
    private static bool IsBeforeBoundary(ExtractedClaim claim, int boundary)
    {
        if (!EvidenceLocator.TryParse(claim.SourceLocator, out int start, out _))
            return true; // No locator → include by default
        return start < boundary;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Returns true if the claim belongs to a step classified as Procedure.
    /// Claims without a step index are included by default.
    /// </summary>
    private static bool IsInProceduralStep(ExtractedClaim claim, IReadOnlyDictionary<int, StepRole> roles)
    {
        if (!claim.StepIndex.HasValue) return true;
        return roles.TryGetValue(claim.StepIndex.Value, out StepRole role) && role == StepRole.Procedure;
    }
}
