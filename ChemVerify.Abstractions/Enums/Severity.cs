namespace ChemVerify.Abstractions.Enums;

/// <summary>
/// Indicates the default severity a validator assigns to its findings.
/// Used by the rule-engine metadata system; does not affect existing scoring.
/// </summary>
public enum Severity
{
    /// <summary>Purely informational — no risk impact.</summary>
    Info,

    /// <summary>Minor concern that may warrant review.</summary>
    Low,

    /// <summary>Notable issue that should be investigated.</summary>
    Medium,

    /// <summary>Serious problem likely to affect scientific validity.</summary>
    High,

    /// <summary>Critical issue that must be resolved before publication.</summary>
    Critical
}
