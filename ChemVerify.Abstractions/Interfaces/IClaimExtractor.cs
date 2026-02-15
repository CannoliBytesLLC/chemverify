using ChemVerify.Abstractions.Models;

namespace ChemVerify.Abstractions.Interfaces;

public interface IClaimExtractor
{
    IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text);
}
