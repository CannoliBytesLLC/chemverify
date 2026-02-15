using Aegis.Core.Models;

namespace Aegis.Core.Interfaces;

public interface IAuditService
{
    Task<AuditArtifact> CreateRunAndAuditAsync(RunCommand command, CancellationToken ct);
}
