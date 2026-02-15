using Aegis.Core.Enums;

namespace Aegis.Infrastructure.Persistence.Entities;

public class ValidationFindingEntity
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

    public AiRunEntity Run { get; set; } = null!;
    public ExtractedClaimEntity? Claim { get; set; }
}
