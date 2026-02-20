using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Abstractions.Validation;

namespace ChemVerify.Core.Validators;

/// <summary>
/// Optional base class for <see cref="IValidator"/> implementations that
/// eliminates boilerplate around <see cref="ValidationFinding"/> creation.
/// <para>
/// Subclasses implement <see cref="ExecuteValidation"/> instead of
/// <see cref="IValidator.Validate"/>. The base class automatically populates
/// <see cref="ValidationFinding.ValidatorName"/> and provides cached access
/// to <see cref="ValidatorMetadataAttribute"/> when present.
/// </para>
/// </summary>
/// <remarks>
/// Migration is incremental — existing validators that implement
/// <see cref="IValidator"/> directly continue to work unchanged.
/// Migrate one at a time by changing <c>: IValidator</c> to <c>: ValidatorBase</c>
/// and replacing the <c>Validate</c> method with <c>ExecuteValidation</c>.
/// </remarks>
public abstract class ValidatorBase : IValidator
{
    /// <summary>
    /// The concrete type name, used to populate
    /// <see cref="ValidationFinding.ValidatorName"/> automatically.
    /// </summary>
    protected string ValidatorName { get; }

    /// <summary>
    /// The <see cref="ValidatorMetadataAttribute"/> applied to this validator,
    /// or <c>null</c> if the attribute is absent. Resolved once at construction.
    /// </summary>
    protected ValidatorMetadataAttribute? Metadata { get; }

    protected ValidatorBase()
    {
        ValidatorName = GetType().Name;
        Metadata = GetType().GetValidatorMetadata();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
        => ExecuteValidation(runId, claims, run);

    /// <summary>
    /// Core validation logic. Implement this instead of <see cref="IValidator.Validate"/>.
    /// </summary>
    protected abstract IReadOnlyList<ValidationFinding> ExecuteValidation(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run);

    // ── Finding construction helpers ─────────────────────────────────

    /// <summary>
    /// Creates a <see cref="ValidationFinding"/> with <see cref="ValidationFinding.Id"/>,
    /// <see cref="ValidationFinding.RunId"/>, and <see cref="ValidationFinding.ValidatorName"/>
    /// pre-populated. All other fields are set via optional parameters.
    /// </summary>
    protected ValidationFinding BuildFinding(
        Guid runId,
        ValidationStatus status,
        string message,
        double confidence,
        Guid? claimId = null,
        string? kind = null,
        string? evidenceRef = null,
        string? jsonPayload = null,
        int? evidenceStartOffset = null,
        int? evidenceEndOffset = null,
        int? evidenceStepIndex = null,
        string? evidenceEntityKey = null,
        string? evidenceSnippet = null) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        ClaimId = claimId,
        ValidatorName = ValidatorName,
        Status = status,
        Message = message,
        Confidence = confidence,
        Kind = kind,
        EvidenceRef = evidenceRef,
        JsonPayload = jsonPayload,
        RuleId = Metadata?.Id ?? ValidatorName,
        RuleVersion = EngineVersionProvider.RuleSetVersion,
        EvidenceStartOffset = evidenceStartOffset,
        EvidenceEndOffset = evidenceEndOffset,
        EvidenceStepIndex = evidenceStepIndex,
        EvidenceEntityKey = evidenceEntityKey,
        EvidenceSnippet = evidenceSnippet
    };

    // ── Evidence reference formatting ────────────────────────────────

    /// <summary>Formats a single-claim evidence reference: <c>Claim:{id}</c>.</summary>
    protected static string FormatClaimRef(Guid claimId) =>
        $"Claim:{claimId}";

    /// <summary>Formats a claim-pair evidence reference: <c>Claim:{a}+Claim:{b}</c>.</summary>
    protected static string FormatClaimPairRef(Guid claimA, Guid claimB) =>
        $"Claim:{claimA}+Claim:{claimB}";

    /// <summary>Formats a text-span evidence reference: <c>AnalyzedText:{start}-{end}</c>.</summary>
    protected static string FormatTextSpanRef(int start, int end) =>
        $"AnalyzedText:{start}-{end}";
}
