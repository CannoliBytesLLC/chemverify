using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Governance;
using ChemVerify.Abstractions.Validation;

namespace ChemVerify.Core.Governance;

/// <summary>
/// Default <see cref="ISeverityCalculator"/>. Resolves severity in this order:
/// <list type="number">
///   <item>Validator metadata override (<see cref="ValidatorMetadataAttribute.DefaultSeverity"/>).</item>
///   <item>Per-kind default from <see cref="SeverityProfiles"/>.</item>
///   <item>Status-based fallback (Fail → Medium, Unverified → Low, Pass → Info).</item>
/// </list>
/// Applies a contextual upgrade for physically-impossible values.
/// </summary>
public sealed class SeverityCalculator : ISeverityCalculator
{
    public Severity Calculate(ValidatorDecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Severity baseSeverity = ResolveBase(context);
        return ApplyContextualUpgrades(baseSeverity, context);
    }

    private static Severity ResolveBase(ValidatorDecisionContext context)
    {
        // 1. Per-kind profile (most specific signal).
        if (!string.IsNullOrEmpty(context.Finding.Kind))
        {
            Severity mapped = SeverityProfiles.GetDefault(context.Finding.Kind, Severity.Medium);
            if (mapped != Severity.Medium || SeverityProfilesContains(context.Finding.Kind))
            {
                return mapped;
            }
        }

        // 2. Validator metadata default.
        if (context.ValidatorMetadata is { } meta)
        {
            return meta.DefaultSeverity;
        }

        // 3. Status-based fallback.
        return context.Finding.Status switch
        {
            Abstractions.Enums.ValidationStatus.Fail => Severity.Medium,
            Abstractions.Enums.ValidationStatus.Unverified => Severity.Low,
            _ => Severity.Info
        };
    }

    private static bool SeverityProfilesContains(string kind) =>
        // Cheap probe: only kinds present in the profiles map should "lock in" the mapped value.
        // This avoids accidentally overriding metadata when a kind isn't mapped.
        SeverityProfiles.GetDefault(kind, Severity.High) != Severity.High
        || SeverityProfiles.GetDefault(kind, Severity.Low) != Severity.Low;

    private static Severity ApplyContextualUpgrades(Severity baseSeverity, ValidatorDecisionContext context)
    {
        // Physical impossibility always escalates to Critical.
        if (context.Finding.Kind == FindingKind.PhysicallyImplausibleValue
            || context.Finding.Kind == FindingKind.ImpossibleRefluxCondition)
        {
            return Severity.Critical;
        }

        return baseSeverity;
    }
}
