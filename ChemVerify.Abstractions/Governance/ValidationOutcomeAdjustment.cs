using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// Immutable description of a deterministic adjustment that the governance
/// precision layer applies to a <see cref="Models.ValidationFinding"/>.
/// </summary>
/// <param name="Suppress">When <c>true</c>, removes the finding from user-facing output and risk scoring.</param>
/// <param name="Reason">Canonical reason code; <see cref="SuppressionReason.None"/> when no change applies.</param>
/// <param name="SeverityOverride">Optional severity override; when <c>null</c> the calculator's value is preserved.</param>
/// <param name="ConfidenceMultiplier">Multiplier in [0, 1] applied to the validator-supplied confidence.</param>
/// <param name="Explanation">Human-readable, audit-friendly justification (no PII).</param>
public readonly record struct ValidationOutcomeAdjustment(
    bool Suppress,
    SuppressionReason Reason,
    Severity? SeverityOverride,
    double ConfidenceMultiplier,
    string Explanation)
{
    /// <summary>The no-op adjustment: leaves the finding unchanged.</summary>
    public static ValidationOutcomeAdjustment None { get; } =
        new(false, SuppressionReason.None, null, 1.0, string.Empty);

    /// <summary>Builds a suppression adjustment with the given reason and explanation.</summary>
    public static ValidationOutcomeAdjustment Suppressed(SuppressionReason reason, string explanation) =>
        new(true, reason, null, 0.0, explanation);

    /// <summary>Builds a confidence-downgrade adjustment with no suppression.</summary>
    public static ValidationOutcomeAdjustment Downgrade(double multiplier, SuppressionReason reason, string explanation) =>
        new(false, reason, null, Math.Clamp(multiplier, 0.0, 1.0), explanation);
}
