namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// Deterministic component that scales the validator-supplied confidence of a
/// finding based on contextual signals (e.g. missing evidence span, low
/// historical precision).
/// </summary>
public interface IConfidenceAdjuster
{
    /// <summary>Stable identifier for audit trails (e.g. "CONFIDENCE_NO_EVIDENCE").</summary>
    string Id { get; }

    /// <summary>
    /// Returns a multiplier in [0, 1] applied to the finding's confidence.
    /// Return 1.0 to abstain.
    /// </summary>
    double GetMultiplier(ValidatorDecisionContext context);
}
