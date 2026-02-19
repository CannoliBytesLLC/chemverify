namespace ChemVerify.Abstractions.Contracts;

/// <summary>
/// Step-aware aggregation of extracted claims, condition clusters, and top issues.
/// Computed deterministically — no ML.
/// </summary>
public class ProcedureSummaryDto
{
    public bool IsProcedural { get; set; }
    public int? ReferencesStartOffset { get; set; }
    public List<StepSummaryDto> Steps { get; set; } = [];
    public List<ConditionClusterDto> Clusters { get; set; } = [];
    public List<TopIssueDto> TopIssues { get; set; } = [];
}

public class StepSummaryDto
{
    public int StepIndex { get; set; }
    public string StepSnippet { get; set; } = string.Empty;
    public string? Role { get; set; }
    public List<ClaimRefDto> Claims { get; set; } = [];
}

public class ClaimRefDto
{
    public string ClaimType { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public string? NormalizedValue { get; set; }
    public string? Unit { get; set; }
    public string? ContextKey { get; set; }
}

public class ConditionClusterDto
{
    public string Label { get; set; } = string.Empty;
    public Dictionary<string, string> Signature { get; set; } = new();
    public List<int> StepIndexes { get; set; } = [];
}

public class TopIssueDto
{
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> Why { get; set; } = [];
    public TopIssueEvidenceDto? Evidence { get; set; }
}

public class TopIssueEvidenceDto
{
    public int? StepIndex { get; set; }
    public string? Snippet { get; set; }
}
