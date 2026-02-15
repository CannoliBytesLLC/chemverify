using ChemVerify.Abstractions.Models;

namespace ChemVerify.Abstractions.Interfaces;

public interface IValidator
{
    IReadOnlyList<ValidationFinding> Validate(Guid runId, IReadOnlyList<ExtractedClaim> claims, AiRun run);
}
