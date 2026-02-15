using ChemVerify.Abstractions.Models;

namespace ChemVerify.Abstractions.Interfaces;

public interface IRiskScorer
{
    double ComputeScore(IReadOnlyList<ValidationFinding> findings, PolicySettings? policy = null);
}
