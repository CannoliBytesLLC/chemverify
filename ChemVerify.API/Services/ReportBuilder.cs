using ChemVerify.Core;
using ChemVerify.Core.Enums;
using ChemVerify.Core.Models;

namespace ChemVerify.API.Services;

/// <summary>
/// Builds a human-readable <see cref="Contracts.ReportDto"/> from raw audit data.
/// Pure logic — no I/O, no AI.
/// </summary>
public static class ReportBuilder
{
    public static Contracts.ReportDto Build(
        double riskScore,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyList<ValidationFinding> findings)
    {
        Contracts.ReportDto report = new()
        {
            Severity = ClassifySeverity(riskScore)
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

        // ── Attention (Fail + MultiScenario) ────────────────────────────
        foreach (ValidationFinding f in findings.Where(f =>
            f.Status == ValidationStatus.Fail))
        {
            report.Attention.Add($"\u274c {HumanizeMessage(f)}");
        }

        foreach (ValidationFinding f in findings.Where(f =>
            f.Kind == FindingKind.MultiScenario))
        {
            report.Attention.Add($"\u26a0\ufe0f {HumanizeMessage(f)}");
        }

        // ── Next Questions ──────────────────────────────────────────────
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

        if (notCheckableValues.Count > 0)
        {
            report.NextQuestions.Add(
                "Single-mention numeric claims could not be cross-checked — is additional data available?");
        }

        // ── Summary + Verdict ───────────────────────────────────────────
        report.Summary = BuildSummary(report, claims.Count);
        report.Verdict = BuildVerdict(report);

        return report;
    }

    private static string ClassifySeverity(double riskScore) => riskScore switch
    {
        <= 0.10 => "Low",
        <= 0.35 => "Medium",
        <= 0.65 => "High",
        _ => "Critical"
    };

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

    private static string BuildVerdict(Contracts.ReportDto report)
    {
        bool hasContradictions = report.Attention.Any(a => a.Contains('\u274c'));
        bool hasMultiScenario = report.Attention.Any(a => a.Contains('\u26a0'));

        if (hasContradictions)
        {
            return "Internal inconsistencies detected. Manual review recommended before relying on this output.";
        }

        if (hasMultiScenario)
        {
            return "Internally consistent; multiple distinct experimental regimes detected.";
        }

        if (report.Confirmed.Count > 0 && report.Attention.Count == 0)
        {
            return "No internal inconsistencies detected. Extracted claims are well-formed and mutually consistent.";
        }

        return "Verification complete. See findings for details.";
    }

    private static string BuildSummary(
        Contracts.ReportDto report,
        int claimCount)
    {
        List<string> parts = [];

        if (report.Attention.Count == 0)
        {
            parts.Add("No internal contradictions detected");
        }
        else if (report.Attention.All(a => a.Contains('\u26a0')))
        {
            parts.Add("No contradictions detected; multiple experimental regimes present");
        }
        else
        {
            parts.Add($"{report.Attention.Count} item(s) require attention");
        }

        if (report.NotVerifiable.Count > 0)
        {
            parts.Add("verification limited by incomplete cross-referencing of numeric parameters");
        }

        parts.Add($"Analysis performed on {claimCount} extracted quantitative and citation claims");

        return string.Join(". ", parts) + ".";
    }
}

