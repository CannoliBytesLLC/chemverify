using ChemVerify.Core.Models;

namespace ChemVerify.Core.Interfaces;

public interface IValidator
{
    IReadOnlyList<ValidationFinding> Validate(Guid runId, IReadOnlyList<ExtractedClaim> claims, AiRun run);
}

