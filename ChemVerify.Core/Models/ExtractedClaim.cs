using ChemVerify.Core.Enums;

namespace ChemVerify.Core.Models;

public class ExtractedClaim
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public ClaimType ClaimType { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string? NormalizedValue { get; set; }
    public string? Unit { get; set; }
    public string? SourceLocator { get; set; }
    public string? JsonPayload { get; set; }
}

