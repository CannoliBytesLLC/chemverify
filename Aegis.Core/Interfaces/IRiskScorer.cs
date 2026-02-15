using Aegis.Core.Models;

namespace Aegis.Core.Interfaces;

public interface IRiskScorer
{
    double ComputeScore(IReadOnlyList<ValidationFinding> findings);
}
