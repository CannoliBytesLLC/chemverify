using ChemVerify.Core.Enums;

namespace ChemVerify.Core.Models;

public class ValidationFinding
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
}

