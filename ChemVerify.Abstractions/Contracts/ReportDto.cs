namespace ChemVerify.Abstractions.Contracts;

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
}

/// <summary>
/// A single explainable risk driver contributing to (or mitigating) the overall risk score.
/// </summary>
public class RiskDriverDto
{
    public double Delta { get; set; }
    public string Label { get; set; } = string.Empty;
}
