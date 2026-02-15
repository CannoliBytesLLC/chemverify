using ChemVerify.Core.Models;

namespace ChemVerify.Core.Interfaces;

public interface IRiskScorer
{
    double ComputeScore(IReadOnlyList<ValidationFinding> findings);
}

