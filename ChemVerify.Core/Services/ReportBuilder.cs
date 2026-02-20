using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Abstractions.Validation;

namespace ChemVerify.Core.Services;

/// <summary>
/// Builds a human-readable <see cref="ReportDto"/> from raw audit data.
/// Pure logic — no I/O, no AI.
/// </summary>
public static class ReportBuilder
{
    public static ReportDto Build(
        double riskScore,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyList<ValidationFinding> findings,
        string? policyProfileName = null,
        string? policyProfileVersion = null)
    {
        string ruleSetVersion = EngineVersionProvider.RuleSetVersion;

        // Back-fill finding-level provenance for findings not created via ValidatorBase
        foreach (ValidationFinding f in findings)
        {
            f.RuleId ??= f.ValidatorName;
            f.RuleVersion ??= ruleSetVersion;
        }

        ReportDto report = new()
        {
            EngineVersion = EngineVersionProvider.GetAssemblyVersion(),
            RuleSetVersion = ruleSetVersion,
            PolicyProfileName = policyProfileName,
            PolicyProfileVersion = policyProfileVersion ?? ruleSetVersion,
            Severity = ClassifySeverity(riskScore, findings)
        };

        // ── Confirmed (Pass findings) ───────────────────────────────────
        int validDois = findings.Count(f =>
            f.ValidatorName == "DoiFormatValidator" && f.Status == ValidationStatus.Pass);

        if (validDois > 0)
        {
            report.Confirmed.Add($"Valid DOI formats detected ({validDois})");
        }

        foreach (ValidationFinding f in findings.Where(f =>
            f.Status == ValidationStatus.Pass && f.Message.Contains('\u2248')))
        {
            report.Confirmed.Add(f.Message);
        }

        foreach (ValidationFinding f in findings.Where(f =>
            f.Status == ValidationStatus.Pass &&
            !f.Message.Contains('\u2248') &&
            f.ValidatorName != "DoiFormatValidator"))
        {
            report.Confirmed.Add(f.Message);
        }

        // ── Not Verifiable (NotCheckable) ───────────────────────────────
        List<string> notCheckableValues = findings
            .Where(f => f.Kind == FindingKind.NotCheckable)
            .Select(f =>
            {
                ExtractedClaim? claim = claims.FirstOrDefault(c => c.Id == f.ClaimId);
                return claim?.RawText;
            })
            .Where(s => s is not null)
            .Cast<string>()
            .ToList();

        if (notCheckableValues.Count > 0)
        {
            report.NotVerifiable.Add(
                $"Single-instance numeric claims ({string.Join(", ", notCheckableValues)}) — no cross-reference available");
        }

        // Kinds that are rendered by category-specific loops below
        HashSet<string> categoryHandledKinds = [
            FindingKind.IncompatibleReagentSolvent,
            FindingKind.MissingSolvent,
            FindingKind.MissingTemperature,
            FindingKind.MissingQuench,
            FindingKind.AmbiguousWorkupTransition,
            FindingKind.EquivInconsistent,
            FindingKind.MalformedChemicalToken,
            FindingKind.UnsupportedOrIncompleteClaim,
            FindingKind.CitationTraceabilityWeak
        ];

        // ── Attention (Fail + MultiScenario) ────────────────────────────
        foreach (ValidationFinding f in findings.Where(f =>
            f.Status == ValidationStatus.Fail &&
            (f.Kind is null || !categoryHandledKinds.Contains(f.Kind))))
        {
            report.Attention.Add($"\u274c {HumanizeMessage(f)}");
            AppendEvidenceLine(report, f);
        }

        foreach (ValidationFinding f in findings.Where(f =>
            f.Kind == FindingKind.MultiScenario))
        {
            report.Attention.Add($"\u26a0\ufe0f {HumanizeMessage(f)}");
            AppendEvidenceLine(report, f);
        }

        foreach (ValidationFinding f in findings.Where(f =>
            f.Kind == FindingKind.CrossStepConditionVariation))
        {
            report.Attention.Add($"\u2139\ufe0f {f.Message}");
        }

        // ── Chemistry-specific attention ─────────────────────────────────
        foreach (ValidationFinding f in findings.Where(f =>
            f.Kind is FindingKind.IncompatibleReagentSolvent or
            FindingKind.MissingSolvent or
            FindingKind.MissingTemperature or
            FindingKind.MissingQuench or
            FindingKind.AmbiguousWorkupTransition or
            FindingKind.EquivInconsistent))
        {
            string icon = f.Kind is FindingKind.IncompatibleReagentSolvent or FindingKind.MissingQuench
                ? "\u2623\ufe0f" : "\u26a0\ufe0f";
            report.Attention.Add($"{icon} {f.Message}");
            AppendEvidenceLine(report, f);
        }

        // ── Text-integrity attention ─────────────────────────────────────
        foreach (ValidationFinding f in findings.Where(f =>
            f.Kind is FindingKind.MalformedChemicalToken or
            FindingKind.UnsupportedOrIncompleteClaim or
            FindingKind.CitationTraceabilityWeak))
        {
            string icon = f.Kind == FindingKind.CitationTraceabilityWeak ? "\U0001f4d1" : "\u26a0\ufe0f";
            report.Attention.Add($"{icon} {f.Message}");

            if (ValidationFindingPayload.TryGetExpectedAndExamples(
                    f.JsonPayload, out string expected, out IReadOnlyList<string> examples))
            {
                string suggestion = examples.Count > 0
                    ? $"   \U0001f4a1 Expected: {expected} (e.g., {string.Join(", ", examples.Take(3))})"
                    : $"   \U0001f4a1 Expected: {expected}";
                report.Attention.Add(suggestion);
            }
            AppendEvidenceLine(report, f);
        }

        // ── Next Questions ──────────────────────────────────────────────
        if (findings.Any(f => f.Kind == FindingKind.IncompatibleReagentSolvent))
        {
            report.NextQuestions.Add(
                "Confirm whether the moisture-sensitive reagent and protic solvent are in the same reaction step.");
        }

        if (findings.Any(f => f.Kind == FindingKind.MissingSolvent))
        {
            report.NextQuestions.Add(
                "What solvent or medium was used for the described procedure?");
        }

        if (findings.Any(f => f.Kind == FindingKind.MissingTemperature))
        {
            report.NextQuestions.Add(
                "What temperature was used for the step that implies thermal control?");
        }

        if (findings.Any(f => f.Kind == FindingKind.MissingQuench))
        {
            report.NextQuestions.Add(
                "Reactive reagent detected without a quench/workup step — how was the reaction terminated safely?");
        }

        if (findings.Any(f => f.Kind == FindingKind.AmbiguousWorkupTransition))
        {
            report.NextQuestions.Add(
                "Dry/inert conditions were followed by aqueous media — is there an explicit workup or extraction step?");
        }

        if (findings.Any(f => f.Kind == FindingKind.EquivInconsistent))
        {
            report.NextQuestions.Add(
                "Stated equivalents do not match the mmol quantities — verify the stoichiometry.");
        }

        if (findings.Any(f => f.Kind == FindingKind.MultiScenario))
        {
            IEnumerable<ExtractedClaim> tempClaims = claims.Where(c =>
                c.JsonPayload is not null && c.JsonPayload.Contains("\"temp\""));
            string temps = string.Join(" and ", tempClaims.Select(c => c.RawText));

            if (temps.Length > 0)
            {
                report.NextQuestions.Add(
                    $"Are {temps} alternative routes or sequential steps?");
            }

            report.NextQuestions.Add(
                "What reagents/solvent correspond to each condition scenario?");
        }

        int failedDois = findings.Count(f =>
            f.ValidatorName == "DoiFormatValidator" && f.Status == ValidationStatus.Fail);
        if (failedDois > 0)
        {
            report.NextQuestions.Add(
                $"{failedDois} DOI(s) failed format validation — are the identifiers correct?");
        }

        if (findings.Any(f => f.Kind == FindingKind.MalformedChemicalToken))
        {
            report.NextQuestions.Add(
                "Malformed chemical tokens detected — verify chemical names and formatting are complete.");
        }

        if (findings.Any(f => f.Kind == FindingKind.UnsupportedOrIncompleteClaim))
        {
            report.NextQuestions.Add(
                "Incomplete scientific claims found — can missing numeric values or citations be supplied?");
        }

        if (findings.Any(f => f.Kind == FindingKind.CitationTraceabilityWeak))
        {
            report.NextQuestions.Add(
                "Mixed citation styles reduce traceability — consider standardising to DOI-only or author-year-only.");
        }

        if (notCheckableValues.Count > 0)
        {
            report.NextQuestions.Add(
                "Single-mention numeric claims could not be cross-checked — is additional data available?");
        }

        // ── Risk drivers ─────────────────────────────────────────────────
        report.RiskDrivers = BuildRiskDrivers(findings);

        // ── Summary + Verdict ───────────────────────────────────────────
        int attentionFindingCount = report.Attention.Count(a => !a.StartsWith("   "));
        report.Summary = BuildSummary(report, claims.Count, attentionFindingCount);
        report.Verdict = BuildVerdict(report, findings);

        return report;
    }

    private static List<RiskDriverDto> BuildRiskDrivers(IReadOnlyList<ValidationFinding> findings)
    {
        List<RiskDriverDto> drivers = [];

        int contradictions = findings.Count(f => f.Kind == FindingKind.Contradiction);
        if (contradictions > 0)
            drivers.Add(new RiskDriverDto { Delta = 0.35, Label = $"{contradictions} unresolved contradiction(s) (same-step)" });

        int chemHigh = findings.Count(f => f.Kind is FindingKind.IncompatibleReagentSolvent or FindingKind.MissingQuench);
        if (chemHigh > 0)
            drivers.Add(new RiskDriverDto { Delta = 0.20, Label = $"{chemHigh} reactive-reagent safety finding(s) (procedure step)" });

        int chemMedium = findings.Count(f => f.Kind is FindingKind.MissingSolvent or FindingKind.MissingTemperature
            or FindingKind.AmbiguousWorkupTransition or FindingKind.EquivInconsistent);
        if (chemMedium > 0)
            drivers.Add(new RiskDriverDto { Delta = 0.15, Label = $"{chemMedium} chemistry completeness finding(s)" });

        int textIntegrity = findings.Count(f => f.Kind is FindingKind.MalformedChemicalToken
            or FindingKind.UnsupportedOrIncompleteClaim or FindingKind.CitationTraceabilityWeak);
        if (textIntegrity > 0)
            drivers.Add(new RiskDriverDto { Delta = 0.10, Label = $"{textIntegrity} text-integrity issue(s)" });

        int multiScenario = findings.Count(f => f.Kind == FindingKind.MultiScenario);
        if (multiScenario > 0)
            drivers.Add(new RiskDriverDto { Delta = 0.0, Label = $"{multiScenario} multi-scenario regime(s) (informational)" });

        int crossStep = findings.Count(f => f.Kind == FindingKind.CrossStepConditionVariation);
        if (crossStep > 0)
            drivers.Add(new RiskDriverDto { Delta = 0.0, Label = $"{crossStep} cross-step condition variation(s) (expected for multistep)" });

        int confirmed = findings.Count(f => f.Status == ValidationStatus.Pass);
        if (confirmed > 0)
            drivers.Add(new RiskDriverDto { Delta = -0.10, Label = $"{confirmed} claim(s) cross-referenced and confirmed" });

        bool hasStepSegmentation = findings.Any(f => f.EvidenceStepIndex.HasValue);
        if (hasStepSegmentation)
            drivers.Add(new RiskDriverDto { Delta = -0.05, Label = "Clear step segmentation detected" });

        return drivers;
    }

    private static string ClassifySeverity(double riskScore, IReadOnlyList<ValidationFinding> findings)
    {
        // If every failed finding is purely text-integrity (formatting noise),
        // cap severity at "Low" regardless of risk score.
        bool hasFailedFindings = findings.Any(f => f.Status == ValidationStatus.Fail);
        if (hasFailedFindings)
        {
            bool allTextIntegrity = findings
                .Where(f => f.Status == ValidationStatus.Fail)
                .All(f => f.Kind is not null && TextIntegrityKinds.Contains(f.Kind));

            if (allTextIntegrity)
                return "Low";
        }

        return riskScore switch
        {
            <= 0.10 => "Low",
            <= 0.35 => "Medium",
            <= 0.65 => "High",
            _ => "Critical"
        };
    }

    private static string HumanizeMessage(ValidationFinding f)
    {
        string msg = f.Message
            .Replace("Possible contradiction:", "Contradiction detected:");

        if (msg.Contains("Multiple scenarios detected"))
        {
            msg = msg.Replace("Multiple scenarios detected",
                "Multiple experimental regimes identified");

            if (!msg.Contains("likely correspond"))
            {
                msg += ". These likely correspond to alternative reaction pathways rather than a single procedure";
            }
        }

        return msg;
    }

    private static void AppendEvidenceLine(ReportDto report, ValidationFinding f)
    {
        if (f.EvidenceSnippet is null && f.EvidenceStartOffset is null)
            return;

        List<string> parts = [];

        if (f.EvidenceSnippet is not null)
            parts.Add($"\"{f.EvidenceSnippet}\"");

        if (f.EvidenceStartOffset is not null && f.EvidenceEndOffset is not null)
            parts.Add($"pos {f.EvidenceStartOffset}-{f.EvidenceEndOffset}");

        if (f.EvidenceStepIndex is not null)
            parts.Add($"step {f.EvidenceStepIndex}");

        if (parts.Count > 0)
            report.Attention.Add($"   \U0001f50e Evidence: {string.Join(", ", parts)}");
    }

    private static readonly HashSet<string> TextIntegrityKinds = new(StringComparer.Ordinal)
    {
        FindingKind.MalformedChemicalToken,
        FindingKind.UnsupportedOrIncompleteClaim,
        FindingKind.CitationTraceabilityWeak
    };

    private static string BuildVerdict(ReportDto report, IReadOnlyList<ValidationFinding> findings)
    {
        bool hasContradictions = report.Attention.Any(a => a.Contains('\u274c'));
        bool hasMultiScenario = findings.Any(f => f.Kind == FindingKind.MultiScenario);

        if (hasContradictions)
        {
            return "Internal inconsistencies detected. Manual review recommended before relying on this output.";
        }

        if (hasMultiScenario)
        {
            return "Internally consistent; multiple distinct experimental regimes detected.";
        }

        // If all attention items come exclusively from text-integrity findings
        bool hasOnlyTextIntegrity = report.Attention.Count > 0
            && findings.Where(f => f.Status == ValidationStatus.Fail || f.Kind is not null)
                       .All(f => f.Status == ValidationStatus.Pass
                              || (f.Kind is not null && TextIntegrityKinds.Contains(f.Kind))
                              || f.Kind == FindingKind.NotCheckable
                              || f.Kind == FindingKind.NotComparable
                              || f.Kind == FindingKind.CrossStepConditionVariation);

        if (hasOnlyTextIntegrity)
        {
            return "Scientific writing/format issues detected. Manual cleanup recommended.";
        }

        if (report.Confirmed.Count > 0 && report.Attention.Count == 0)
        {
            return "No internal inconsistencies detected. Extracted claims are well-formed and mutually consistent.";
        }

        return "Verification complete. See findings for details.";
    }

    private static string BuildSummary(
        ReportDto report,
        int claimCount,
        int attentionFindingCount)
    {
        List<string> parts = [];

        if (attentionFindingCount == 0)
        {
            parts.Add("No internal contradictions detected");
        }
        else if (report.Attention.All(a => a.Contains('\u26a0')))
        {
            parts.Add("No contradictions detected; multiple experimental regimes present");
        }
        else
        {
            parts.Add($"{attentionFindingCount} item(s) require attention");
        }

        if (report.NotVerifiable.Count > 0)
        {
            parts.Add("verification limited by incomplete cross-referencing of numeric parameters");
        }

        parts.Add($"Analysis performed on {claimCount} extracted quantitative and citation claims");

        return string.Join(". ", parts) + ".";
    }
}

