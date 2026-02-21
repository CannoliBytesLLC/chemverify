using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Tests;

/// <summary>
/// Maps human-readable expectation strings from the fixture corpus to concrete
/// assertions against extracted claims and validation findings.
/// </summary>
internal static class ExpectationAsserts
{
    /// <summary>
    /// Result of evaluating a single expectation string.
    /// </summary>
    /// <param name="Mapped">True if the expectation was recognized by the DSL mapper.</param>
    /// <param name="Passed">True if the mapped assertion passed (always true for unmapped).</param>
    /// <param name="Description">Human-readable description of what was checked or why it was skipped.</param>
    internal record AssertionOutcome(bool Mapped, bool Passed, string Description);

    /// <summary>
    /// Evaluates a single expectation string against the engine output.
    /// Compound expectations joined by " and " are split and each part evaluated.
    /// </summary>
    public static IReadOnlyList<AssertionOutcome> Evaluate(
        string expectation,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyList<ValidationFinding> findings)
    {
        // Handle compound "X and Y" expectations
        string[] parts = Regex.Split(expectation, @"\s+and\s+", RegexOptions.IgnoreCase);
        if (parts.Length > 1)
        {
            List<AssertionOutcome> results = [];
            foreach (string part in parts)
                results.AddRange(Evaluate(part.Trim(), claims, findings));
            return results;
        }

        return [EvaluateSingle(expectation.Trim(), claims, findings)];
    }

    private static AssertionOutcome EvaluateSingle(
        string expectation,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyList<ValidationFinding> findings)
    {
        string norm = expectation.Trim();

        // Handle OR alternatives: pass if any branch passes
        if (Regex.IsMatch(norm, @"\s+OR\s+", RegexOptions.IgnoreCase))
        {
            string[] alternatives = Regex.Split(norm, @"\s+OR\s+", RegexOptions.IgnoreCase);
            foreach (string alt in alternatives)
            {
                AssertionOutcome result = EvaluateAtomic(alt.Trim(), claims, findings);
                if (result.Mapped && result.Passed)
                    return result with { Description = $"OR-branch passed: {result.Description}" };
            }

            // If all branches failed, try to find a mapped one to report
            foreach (string alt in alternatives)
            {
                AssertionOutcome result = EvaluateAtomic(alt.Trim(), claims, findings);
                if (result.Mapped)
                    return result with { Description = $"All OR-branches failed; last: {result.Description}" };
            }

            return new AssertionOutcome(false, true, $"UNMAPPED (OR): {norm}");
        }

        return EvaluateAtomic(norm, claims, findings);
    }

    // Strips trailing parenthetical notes like "(~8)", "(MW ~1,000,000)", "(tolerance allows)"
    // These are informational in the corpus but not part of the assertion logic.
    private static readonly Regex TrailingNoteRegex = new(
        @"\s*\([^)]*\)\s*$", RegexOptions.Compiled);

    private static AssertionOutcome EvaluateAtomic(
        string rawText,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyList<ValidationFinding> findings)
    {
        // ── Conditional expectations → UNMAPPED ─────────────────────────
        // "unless catalog contains", "if supported, else skip", "if entity key present, else SKIP"
        if (Regex.IsMatch(rawText, @"\bunless\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(rawText, @"\bif\s+.+,?\s*else\b", RegexOptions.IgnoreCase))
        {
            return new AssertionOutcome(false, true,
                $"UNMAPPED (conditional): {rawText}");
        }

        // Strip trailing parenthetical notes for pattern matching
        string text = TrailingNoteRegex.Replace(rawText, "").Trim();

        // ── SymbolicTemperature claims ──────────────────────────────────

        Match m = Regex.Match(text,
            @"(?:Has\s+)?SymbolicTemperature\((\w+)\)(?:\s+claim\s+exists)?",
            RegexOptions.IgnoreCase);
        if (m.Success)
        {
            string expected = m.Groups[1].Value.ToLowerInvariant();
            bool found = claims.Any(c =>
                c.ClaimType == ClaimType.SymbolicTemperature
                && string.Equals(c.NormalizedValue, expected, StringComparison.OrdinalIgnoreCase));
            return new AssertionOutcome(true, found,
                found ? $"SymbolicTemperature({expected}) found"
                      : $"SymbolicTemperature({expected}) NOT found");
        }

        // ── Numeric temp claim ──────────────────────────────────────────

        if (Regex.IsMatch(text, @"(?:Has\s+)?[Nn]umeric\s+temp\s+claim\s+exists?", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"Has\s+numeric\s+temp\s+claim", RegexOptions.IgnoreCase))
        {
            bool found = claims.Any(c =>
                c.ClaimType == ClaimType.NumericWithUnit
                && c.JsonPayload is not null
                && c.JsonPayload.Contains("\"temp\"", StringComparison.OrdinalIgnoreCase));
            return new AssertionOutcome(true, found,
                found ? "Numeric temp claim found" : "Numeric temp claim NOT found");
        }

        // ── Composition claim (%) ───────────────────────────────────────

        if (Regex.IsMatch(text, @"Has\s+composition\s+claim", RegexOptions.IgnoreCase))
        {
            bool found = claims.Any(c =>
                c.ClaimType == ClaimType.NumericWithUnit && c.Unit == "%");
            return new AssertionOutcome(true, found,
                found ? "Composition claim (%) found" : "Composition claim (%) NOT found");
        }

        // ── AtmosphereCondition ─────────────────────────────────────────

        if (Regex.IsMatch(text, @"No\s+AtmosphereCondition\s+claim", RegexOptions.IgnoreCase))
        {
            bool absent = !claims.Any(c => c.ClaimType == ClaimType.AtmosphereCondition);
            return new AssertionOutcome(true, absent,
                absent ? "No AtmosphereCondition claim (correct)"
                       : "AtmosphereCondition claim found unexpectedly");
        }

        if (Regex.IsMatch(text, @"AtmosphereCondition\s+extracted", RegexOptions.IgnoreCase))
        {
            bool found = claims.Any(c => c.ClaimType == ClaimType.AtmosphereCondition);
            return new AssertionOutcome(true, found,
                found ? "AtmosphereCondition claim found"
                      : "AtmosphereCondition claim NOT found");
        }

        // ── NumericWithUnit for specific units ──────────────────────────

        m = Regex.Match(text, @"NumericWithUnit\s+claim\s+exists\s+for\s+(\S+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            string unit = m.Groups[1].Value;
            bool found = claims.Any(c =>
                c.ClaimType == ClaimType.NumericWithUnit
                && string.Equals(c.Unit, unit, StringComparison.OrdinalIgnoreCase));
            return new AssertionOutcome(true, found,
                found ? $"NumericWithUnit({unit}) claim found"
                      : $"NumericWithUnit({unit}) claim NOT found");
        }

        // ── PlaceholderToken findings ───────────────────────────────────

        if (Regex.IsMatch(text, @"PlaceholderToken(?:Validator)?\s+should\s+FAIL", RegexOptions.IgnoreCase))
        {
            bool found = findings.Any(f =>
                f.Kind is FindingKind.PlaceholderOrMissingToken or FindingKind.MalformedChemicalToken);
            return new AssertionOutcome(true, found,
                found ? "PlaceholderOrMissingToken (or equivalent MalformedChemicalToken) finding present"
                      : "PlaceholderOrMissingToken finding NOT present (expected)");
        }

        if (Regex.IsMatch(text, @"PlaceholderToken(?:Validator)?\s+should\s+PASS", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"No\s+PlaceholderToken\s+findings", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.Kind == FindingKind.PlaceholderOrMissingToken);
            return new AssertionOutcome(true, absent,
                absent ? "No PlaceholderOrMissingToken findings (correct)"
                       : "PlaceholderOrMissingToken finding present unexpectedly");
        }

        // ── MissingTemperature ──────────────────────────────────────────

        if (Regex.IsMatch(text, @"No\s+(?:MissingTemperature|implied\s+temperature\s+finding)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"No\s+MissingTemperatureWhenImplied", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.Kind == FindingKind.MissingTemperature);
            return new AssertionOutcome(true, absent,
                absent ? "No MissingTemperature finding (correct)"
                       : "MissingTemperature finding present unexpectedly");
        }

        // ── DryInertMismatch / Workup transition ────────────────────────

        if (Regex.IsMatch(text, @"No\s+DryInertMismatch", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.Kind == FindingKind.AmbiguousWorkupTransition);
            return new AssertionOutcome(true, absent,
                absent ? "No AmbiguousWorkupTransition finding (correct)"
                       : "AmbiguousWorkupTransition finding present unexpectedly");
        }

        if (Regex.IsMatch(text, @"[Ss]hould\s+trigger\s+DryInertMismatch", RegexOptions.IgnoreCase))
        {
            bool found = findings.Any(f => f.Kind == FindingKind.AmbiguousWorkupTransition);
            return new AssertionOutcome(true, found,
                found ? "AmbiguousWorkupTransition finding present"
                      : "AmbiguousWorkupTransition finding NOT present (expected)");
        }

        if (Regex.IsMatch(text, @"Workup\s+transition\s+present", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.Kind == FindingKind.AmbiguousWorkupTransition);
            return new AssertionOutcome(true, absent,
                absent ? "No AmbiguousWorkupTransition (workup transition recognized)"
                       : "AmbiguousWorkupTransition fired despite expected workup transition");
        }

        if (Regex.IsMatch(text, @"No\s+DryInertMismatch\s+due\s+to\s+narrative\s+gating", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.Kind == FindingKind.AmbiguousWorkupTransition);
            return new AssertionOutcome(true, absent,
                absent ? "No AmbiguousWorkupTransition (narrative gating correct)"
                       : "AmbiguousWorkupTransition fired despite narrative gating");
        }

        // ── MwConsistency ───────────────────────────────────────────────

        if (Regex.IsMatch(text, @"MwConsistency\s+should\s+PASS", RegexOptions.IgnoreCase))
        {
            bool found = findings.Any(f => f.Kind == FindingKind.MwConsistent);
            return new AssertionOutcome(true, found,
                found ? "MwConsistent finding present"
                      : "MwConsistent finding NOT present");
        }

        if (Regex.IsMatch(text, @"MwConsistency\s+should\s+FAIL", RegexOptions.IgnoreCase))
        {
            bool found = findings.Any(f => f.Kind == FindingKind.MwImplausible);
            return new AssertionOutcome(true, found,
                found ? "MwImplausible finding present"
                      : "MwImplausible finding NOT present (expected)");
        }

        if (Regex.IsMatch(text, @"MwConsistency\s+should\s+SKIP", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f =>
                f.Kind is FindingKind.MwConsistent or FindingKind.MwImplausible);
            return new AssertionOutcome(true, absent,
                absent ? "No MW findings (correctly skipped)"
                       : "MW findings present when expected skip");
        }

        // ── YieldMassConsistency ────────────────────────────────────────

        if (Regex.IsMatch(text, @"YieldMassConsistency\s+should\s+FAIL", RegexOptions.IgnoreCase))
        {
            bool found = findings.Any(f => f.Kind == FindingKind.YieldMassInconsistent);
            return new AssertionOutcome(true, found,
                found ? "YieldMassInconsistent finding present"
                      : "YieldMassInconsistent finding NOT present (expected)");
        }

        if (Regex.IsMatch(text, @"YieldMassConsistency\s+should\s+SKIP", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.Kind == FindingKind.YieldMassInconsistent);
            return new AssertionOutcome(true, absent,
                absent ? "No YieldMassInconsistent finding (correctly skipped)"
                       : "YieldMassInconsistent finding present unexpectedly");
        }

        if (Regex.IsMatch(text, @"[Ss]hould\s+NOT\s+fail", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.Kind == FindingKind.YieldMassInconsistent);
            return new AssertionOutcome(true, absent,
                absent ? "No YieldMassInconsistent finding (correct)"
                       : "YieldMassInconsistent finding present unexpectedly");
        }

        // ── ConcentrationSanity ─────────────────────────────────────────

        if (Regex.IsMatch(text, @"ConcentrationSanity\s+(?:PASS|pass\s+for)", RegexOptions.IgnoreCase))
        {
            bool found = findings.Any(f =>
                f.ValidatorName == "ConcentrationSanityValidator"
                && f.Status == ValidationStatus.Pass);
            return new AssertionOutcome(true, found,
                found ? "ConcentrationSanity pass finding present"
                      : "ConcentrationSanity pass finding NOT present");
        }

        if (Regex.IsMatch(text, @"ConcentrationSanity\s+(?:should\s+SKIP|likely\s+SKIP)", RegexOptions.IgnoreCase))
        {
            bool absent = !findings.Any(f => f.ValidatorName == "ConcentrationSanityValidator");
            return new AssertionOutcome(true, absent,
                absent ? "No ConcentrationSanity findings (correctly skipped)"
                       : "ConcentrationSanity findings present unexpectedly");
        }

        // ── Procedural context (cannot assert from endpoint; mark unmapped) ─

        if (Regex.IsMatch(text, @"ProceduralContextDetector\s+should\s+be\s+NOT\s+procedural", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"[Hh]edge\s+dampener\s+should\s+suppress", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"[Nn]ot\s+(?:a\s+)?procedural?(?:\b|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"[Ss]hould\s+be\s+NOT\s+procedural", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"[Nn]ot\s+a\s+procedure\b", RegexOptions.IgnoreCase))
        {
            return new AssertionOutcome(false, true,
                $"UNMAPPED (procedural context not exposed via endpoint): {text}");
        }

        // ── Generic catch-alls ──────────────────────────────────────────

        if (Regex.IsMatch(text, @"ensure\s+chemistry\s+validators\s+don.t\s+overfire", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"skip\s+gracefully", RegexOptions.IgnoreCase))
        {
            return new AssertionOutcome(false, true,
                $"UNMAPPED (qualitative/subjective): {text}");
        }

        // ── Truly unmapped ──────────────────────────────────────────────

        return new AssertionOutcome(false, true, $"UNMAPPED: {text}");
    }
}
