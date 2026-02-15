using ChemVerify.Core.Enums;

namespace ChemVerify.API.Contracts;

public class RunSummary
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public RunStatus Status { get; set; }
    public double RiskScore { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string? UserId { get; set; }
}

