using ChemVerify.Abstractions.Models;
using ChemVerify.Abstractions.Validation;

namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// Deterministic context passed to <see cref="IFindingSuppressor"/>,
/// <see cref="IConfidenceAdjuster"/>, and <see cref="ISeverityCalculator"/>
/// implementations. Carries the run, the finding under evaluation, the
/// originating validator metadata, and an opaque <see cref="StateBag"/>
/// produced by the procedure-state replay layer.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="StateBag"/> is intentionally typed as <c>object?</c> so the
/// abstractions project does not depend on Core's <c>ProcedureStateEngine</c>.
/// Concrete suppressors in Core cast it to the expected snapshot type.
/// </para>
/// </remarks>
public sealed class ValidatorDecisionContext
{
    public required AiRun Run { get; init; }
    public required IReadOnlyList<ExtractedClaim> Claims { get; init; }
    public required IReadOnlyList<ValidationFinding> AllFindings { get; init; }
    public required ValidationFinding Finding { get; init; }
    public ValidatorMetadataAttribute? ValidatorMetadata { get; init; }
    public PolicySettings? Policy { get; init; }

    /// <summary>
    /// Replayed procedure-state snapshots, when available. Suppressors that
    /// require state context cast this to <c>IReadOnlyList&lt;StateSnapshot&gt;</c>.
    /// </summary>
    public object? StateBag { get; init; }
}
