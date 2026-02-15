using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Infrastructure.Persistence.Entities;

public class ExtractedClaimEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public ClaimType ClaimType { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string? NormalizedValue { get; set; }
    public string? Unit { get; set; }
    public string? SourceLocator { get; set; }
    public string? JsonPayload { get; set; }
    public string? EntityKey { get; set; }
    public int? StepIndex { get; set; }

    public AiRunEntity Run { get; set; } = null!;
}

