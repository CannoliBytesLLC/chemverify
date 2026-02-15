using ChemVerify.Abstractions.Models;

namespace ChemVerify.Abstractions.Interfaces;

public interface IAuditService
{
    Task<AuditArtifact> CreateRunAndAuditAsync(RunCommand command, CancellationToken ct);
    Task<AuditArtifact> VerifyTextAsync(string textToVerify, string? policyProfile, CancellationToken ct);
}
