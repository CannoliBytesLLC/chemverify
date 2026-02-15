namespace ChemVerify.Abstractions.Models;

public class AuditArtifact
{
    public Guid RunId { get; set; }
    public AiRun Run { get; set; } = null!;
    public IReadOnlyList<ExtractedClaim> Claims { get; set; } = [];
    public IReadOnlyList<ValidationFinding> Findings { get; set; } = [];
    public string ArtifactHash { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; set; }
}
