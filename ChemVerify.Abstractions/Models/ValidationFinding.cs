using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Models;

public class ValidationFinding
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid? ClaimId { get; set; }
    public string ValidatorName { get; set; } = string.Empty;
    public ValidationStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? EvidenceRef { get; set; }
    public string? Kind { get; set; }
    public string? JsonPayload { get; set; }

    // ── Classification ────────────────────────────────────────────────
    /// <summary>
    /// Whether this finding is user-facing (<see cref="FindingCategory.Finding"/>)
    /// or an internal diagnostic (<see cref="FindingCategory.Diagnostic"/>).
    /// Defaults to <see cref="FindingCategory.Finding"/> for backward compatibility.
    /// </summary>
    public FindingCategory Category { get; set; } = FindingCategory.Finding;

    /// <summary>Convenience accessor: <c>true</c> when <see cref="Category"/> is <see cref="FindingCategory.Diagnostic"/>.</summary>
    public bool IsDiagnostic => Category == FindingCategory.Diagnostic;

    // ── Provenance (populated by ValidatorBase / ReportBuilder) ───
    public string? RuleId { get; set; }
    public string? RuleVersion { get; set; }

    // ── Evidence span (populated post-validation) ────────────────────
    public int? EvidenceStartOffset { get; set; }
    public int? EvidenceEndOffset { get; set; }
    public int? EvidenceStepIndex { get; set; }
    public string? EvidenceEntityKey { get; set; }
    public string? EvidenceSnippet { get; set; }
}
