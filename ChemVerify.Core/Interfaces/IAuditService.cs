using ChemVerify.Core.Models;

namespace ChemVerify.Core.Interfaces;

public interface IAuditService
{
    Task<AuditArtifact> CreateRunAndAuditAsync(RunCommand command, CancellationToken ct);
}

