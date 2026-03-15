using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// A single expected finding within a <see cref="GoldSetItem"/>.
/// Represents what a reviewer determined the correct validator output should be.
/// </summary>
/// <param name="ValidatorName">Fully qualified validator type name (e.g. "MwConsistencyValidator").</param>
/// <param name="ExpectedStatus">The correct verdict the validator should produce.</param>
/// <param name="ExpectedKind">
/// The <see cref="FindingKind"/> the validator should emit, or <c>null</c> if any kind is acceptable.
/// </param>
/// <param name="SeverityOverride">
/// Reviewer override for severity when the default is considered wrong for this case.
/// </param>
/// <param name="Notes">Free-text reviewer notes explaining the expected outcome.</param>
public sealed record ExpectedFinding(
    string ValidatorName,
    ValidationStatus ExpectedStatus,
    string? ExpectedKind = null,
    Severity? SeverityOverride = null,
    string? Notes = null);
