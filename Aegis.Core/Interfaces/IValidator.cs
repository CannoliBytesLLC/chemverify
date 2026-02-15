using Aegis.Core.Models;

namespace Aegis.Core.Interfaces;

public interface IValidator
{
    IReadOnlyList<ValidationFinding> Validate(Guid runId, IReadOnlyList<ExtractedClaim> claims, AiRun run);
}
