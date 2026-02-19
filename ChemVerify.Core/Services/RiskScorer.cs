using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Services;

public class RiskScorer : IRiskScorer
{
    private const double FailWeight = 1.0;
    private const double UnverifiedWeight = 0.3;
    private const double NotCheckableWeight = 0.05;
    private const double PassWeight = 0.0;

    private const double ChemHighWeight = 0.35;
    private const double ChemMediumWeight = 0.15;

    private const double TextIntegrityWeight = 0.10;
    private const double DampenedDoiFailWeight = 0.15;

    private static readonly HashSet<string> ChemHighKinds = new(StringComparer.Ordinal)
    {
        FindingKind.IncompatibleReagentSolvent,
        FindingKind.MissingQuench
    };

    private static readonly HashSet<string> ChemMediumKinds = new(StringComparer.Ordinal)
    {
        FindingKind.MissingSolvent,
        FindingKind.MissingTemperature,
        FindingKind.AmbiguousWorkupTransition,
        FindingKind.EquivInconsistent
    };

    private static readonly HashSet<string> TextIntegrityKinds = new(StringComparer.Ordinal)
    {
        FindingKind.MalformedChemicalToken,
        FindingKind.UnsupportedOrIncompleteClaim,
        FindingKind.CitationTraceabilityWeak
    };

    public double ComputeScore(IReadOnlyList<ValidationFinding> findings, PolicySettings? policy = null)
    {
        if (findings.Count == 0)
        {
            return 0.0;
        }

        bool dampenDoi = policy?.DampenDoiFailSeverity == true;

        // Separate chemistry / text-integrity findings from general findings
        List<ValidationFinding> general = new();
        double chemAdditiveScore = 0.0;
        double textIntegrityAdditiveScore = 0.0;

        foreach (ValidationFinding f in findings)
        {
            if (f.Kind is not null && ChemHighKinds.Contains(f.Kind))
            {
                chemAdditiveScore += ChemHighWeight;
            }
            else if (f.Kind is not null && ChemMediumKinds.Contains(f.Kind))
            {
                chemAdditiveScore += ChemMediumWeight;
            }
            else if (f.Kind is not null && TextIntegrityKinds.Contains(f.Kind))
            {
                textIntegrityAdditiveScore += TextIntegrityWeight;
            }
            else
            {
                general.Add(f);
            }
        }

        double baseScore = 0.0;
        if (general.Count > 0)
        {
            double total = general.Sum(f => f.Status switch
            {
                ValidationStatus.Fail when dampenDoi && f.ValidatorName == "DoiFormatValidator"
                    => DampenedDoiFailWeight,
                ValidationStatus.Fail => FailWeight,
                ValidationStatus.Unverified => f.Kind is FindingKind.NotCheckable or FindingKind.NotComparable ? NotCheckableWeight : UnverifiedWeight,
                _ => PassWeight
            });

            baseScore = total / general.Count;
        }

        return Math.Clamp(baseScore + chemAdditiveScore + textIntegrityAdditiveScore, 0.0, 1.0);
    }
}

