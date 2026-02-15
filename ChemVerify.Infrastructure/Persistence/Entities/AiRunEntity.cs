using ChemVerify.Core.Enums;

namespace ChemVerify.Infrastructure.Persistence.Entities;

public class AiRunEntity
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

    public ICollection<ExtractedClaimEntity> Claims { get; set; } = new List<ExtractedClaimEntity>();
    public ICollection<ValidationFindingEntity> Findings { get; set; } = new List<ValidationFindingEntity>();
}

