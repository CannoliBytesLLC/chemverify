namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// Deterministic, side-effect-free decision component that may suppress or
/// downgrade a <see cref="Models.ValidationFinding"/> based on the procedural
/// or chemistry context.
/// </summary>
/// <remarks>
/// Implementations MUST be pure functions of their inputs and MUST NOT mutate
/// the supplied context. They run in registration order; the first non-<see
/// cref="ValidationOutcomeAdjustment.None"/> result wins.
/// </remarks>
public interface IFindingSuppressor
{
    /// <summary>Stable identifier for audit trails (e.g. "SUPPRESS_SEALED_SYSTEM").</summary>
    string Id { get; }

    /// <summary>Returns an adjustment, or <see cref="ValidationOutcomeAdjustment.None"/> to abstain.</summary>
    ValidationOutcomeAdjustment Evaluate(ValidatorDecisionContext context);
}
