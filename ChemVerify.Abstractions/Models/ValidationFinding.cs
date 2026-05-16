using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Governance;

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

    // ── Governance precision layer (optional, populated by GovernanceProcessor) ──
    /// <summary>
    /// Effective severity assigned by the governance precision layer.
    /// Null when the governance pipeline has not run for this finding.
    /// </summary>
    public Severity? Severity { get; set; }

    /// <summary>
    /// Validator-supplied confidence after deterministic adjustment by
    /// <see cref="Governance.IConfidenceAdjuster"/> components.
    /// Null when no adjustment was applied.
    /// </summary>
    public double? AdjustedConfidence { get; set; }

    /// <summary>
    /// True when a <see cref="Governance.IFindingSuppressor"/> requested that
    /// this finding be hidden from user-facing output and excluded from risk scoring.
    /// </summary>
    public bool IsSuppressed { get; set; }

    /// <summary>
    /// Canonical reason code for suppression or downgrade. Null when no
    /// adjustment was applied; otherwise one of <see cref="SuppressionReason"/>
    /// rendered via <c>ToString()</c>.
    /// </summary>
    public string? SuppressionReasonCode { get; set; }

    /// <summary>
    /// Human-readable explanation for the governance adjustment, suitable
    /// for inclusion in audit/compliance reports.
    /// </summary>
    public string? AdjustmentExplanation { get; set; }

    /// <summary>
    /// Identifier of the <see cref="Governance.FindingCluster"/> this finding
    /// belongs to, when clustering has been applied.
    /// </summary>
    public string? ClusterId { get; set; }
}
