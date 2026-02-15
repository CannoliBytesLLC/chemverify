using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Infrastructure.Persistence.Entities;

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
    public string? JsonPayload { get; set; }

    // ── Evidence span ────────────────────────────────────────────────
    public int? EvidenceStartOffset { get; set; }
    public int? EvidenceEndOffset { get; set; }
    public int? EvidenceStepIndex { get; set; }
    public string? EvidenceEntityKey { get; set; }
    public string? EvidenceSnippet { get; set; }

    public AiRunEntity Run { get; set; } = null!;
    public ExtractedClaimEntity? Claim { get; set; }
}

