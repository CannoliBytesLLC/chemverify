namespace ChemVerify.API.Contracts;

/// <summary>
/// Human-readable verification report derived deterministically from findings.
/// </summary>
public class ReportDto
{
    public string Severity { get; set; } = string.Empty;
    public string Verdict { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Confirmed { get; set; } = [];
    public List<string> NotVerifiable { get; set; } = [];
    public List<string> Attention { get; set; } = [];
    public List<string> NextQuestions { get; set; } = [];
}

