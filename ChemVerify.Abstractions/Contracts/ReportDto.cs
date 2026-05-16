namespace ChemVerify.Abstractions.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Human-readable verification report derived deterministically from findings.
/// </summary>
public class ReportDto
{
    // ── Provenance ──────────────────────────────────────────────────
    public string EngineVersion { get; set; } = string.Empty;
    public string RuleSetVersion { get; set; } = string.Empty;
    public string? PolicyProfileName { get; set; }
    public string? PolicyProfileVersion { get; set; }

    // ── Report body ─────────────────────────────────────────────────
    public string Severity { get; set; } = string.Empty;
    public string Verdict { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Confirmed { get; set; } = [];
    public List<string> NotVerifiable { get; set; } = [];
    public List<string> Attention { get; set; } = [];
    public List<string> NextQuestions { get; set; } = [];
    public List<RiskDriverDto> RiskDrivers { get; set; } = [];

    // ── Governance section ─────────────────────────────────────────
    /// <summary>Optional governance-style overlay; null when no governance findings were produced.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GovernanceReportSection? Governance { get; set; }
}

/// <summary>
/// A single explainable risk driver contributing to (or mitigating) the overall risk score.
/// </summary>
public class RiskDriverDto
{
    public double Delta { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Governance overlay attached to a <see cref="ReportDto"/>. Surfaces clusters,
/// suppressions, validator confidence, and severity distribution so reports
/// resemble enterprise audit/QA outputs rather than chemistry toy outputs.
/// </summary>
public class GovernanceReportSection
{
    public Dictionary<string, int> SeverityHistogram { get; set; } = new();
    public List<string> TopRiskDrivers { get; set; } = [];
    public List<ClusteredFindingDto> Clusters { get; set; } = [];
    public List<SuppressionExplanationDto> Suppressions { get; set; } = [];
    public List<ValidatorConfidenceDto> ValidatorConfidence { get; set; } = [];
    public List<ProcedureTimelineEntryDto> ProcedureTimeline { get; set; } = [];
}

public class ClusteredFindingDto
{
    public string ClusterId { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public string AggregatedSeverity { get; set; } = string.Empty;
    public int FindingCount { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public double RiskContribution { get; set; }
    public List<int> SharedSteps { get; set; } = [];
}

public class SuppressionExplanationDto
{
    public string ValidatorName { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public int? StepIndex { get; set; }
}

public class ValidatorConfidenceDto
{
    public string ValidatorName { get; set; } = string.Empty;
    public int TriggerFrequency { get; set; }
    public double PrecisionEstimate { get; set; }
    public double SuppressionRate { get; set; }
    public double DowngradeRate { get; set; }
    public double HighSeverityHitRate { get; set; }
}

public class ProcedureTimelineEntryDto
{
    public int StepIndex { get; set; }
    public string Phase { get; set; } = string.Empty;
    public List<string> Transitions { get; set; } = [];
}
