using Aegis.Core.Enums;

namespace Aegis.Core.Models;

public class AiRun
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public RunStatus Status { get; set; }
    public string? UserId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string? PolicyProfile { get; set; }
    public string? ConnectorName { get; set; }
    public string? ModelVersion { get; set; }
    public string? ParametersJson { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string? PreviousHash { get; set; }
    public string CurrentHash { get; set; } = string.Empty;
    public double RiskScore { get; set; }
}
