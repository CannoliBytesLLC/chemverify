using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.API.Contracts;

public class CreateRunResponse
{
    public Guid RunId { get; set; }
    public double RiskScore { get; set; }
    public ReportDto Report { get; set; } = null!;
    public AuditArtifact Artifact { get; set; } = null!;
}

