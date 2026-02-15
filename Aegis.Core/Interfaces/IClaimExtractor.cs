using Aegis.Core.Models;

namespace Aegis.Core.Interfaces;

public interface IClaimExtractor
{
    IReadOnlyList<ExtractedClaim> Extract(Guid runId, string text);
}
