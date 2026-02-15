using ChemVerify.Core.Models;

namespace ChemVerify.Core.Interfaces;

public interface IClaimExtractor
{
    IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text);
}

