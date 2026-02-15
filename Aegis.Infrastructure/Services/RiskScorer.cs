using Aegis.Core;
using Aegis.Core.Enums;
using Aegis.Core.Interfaces;
using Aegis.Core.Models;

namespace Aegis.Infrastructure.Services;

public class RiskScorer : IRiskScorer
{
    private const double FailWeight = 1.0;
    private const double UnverifiedWeight = 0.3;
    private const double NotCheckableWeight = 0.05;
    private const double PassWeight = 0.0;

    public double ComputeScore(IReadOnlyList<ValidationFinding> findings)
    {
        if (findings.Count == 0)
        {
            return 0.0;
        }

        double total = findings.Sum(f => f.Status switch
        {
            ValidationStatus.Fail => FailWeight,
            ValidationStatus.Unverified => f.Kind == FindingKind.NotCheckable ? NotCheckableWeight : UnverifiedWeight,
            _ => PassWeight
        });

        return Math.Clamp(total / findings.Count, 0.0, 1.0);
    }
}
