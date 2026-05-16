using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Governance;

namespace ChemVerify.Core.Governance.Suppressors;

/// <summary>
/// Suppresses NumericContradictionValidator-style temperature/time variation
/// findings when the differing values originate from distinct procedural steps
/// — that is the expected pattern in multistep synthesis (e.g., 0 °C addition
/// then 60 °C heating).
/// </summary>
/// <remarks>
/// This is a contextual exemption, not a precision downgrade: the underlying
/// validator behavior is unchanged, but the governance layer recognizes the
/// pattern and mutes the user-facing finding.
/// </remarks>
public sealed class CrossStepVariationSuppressor : IFindingSuppressor
{
    public string Id => "SUPPRESS_CROSS_STEP_VARIATION";

    public ValidationOutcomeAdjustment Evaluate(ValidatorDecisionContext context)
    {
        if (context.Finding.Kind != FindingKind.Contradiction)
        {
            return ValidationOutcomeAdjustment.None;
        }

        if (context.Finding.ValidatorName != "NumericContradictionValidator")
        {
            return ValidationOutcomeAdjustment.None;
        }

        // Only suppress when the message explicitly references multiple steps.
        // The validator emits step indices in its message when it crosses
        // step boundaries; absent that, treat as same-step contradiction.
        string msg = context.Finding.Message ?? string.Empty;
        if (!msg.Contains("step", StringComparison.OrdinalIgnoreCase)
            || !msg.Contains("vs", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationOutcomeAdjustment.None;
        }

        return ValidationOutcomeAdjustment.Suppressed(
            SuppressionReason.CrossStepVariationExpected,
            "Numeric variation occurs across distinct procedural steps; multistep synthesis routinely uses different conditions per step.");
    }
}
