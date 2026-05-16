using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// Computes the effective severity of a finding from its kind, validator
/// metadata, and contextual signals. Implementations must be deterministic.
/// </summary>
public interface ISeverityCalculator
{
    Severity Calculate(ValidatorDecisionContext context);
}
