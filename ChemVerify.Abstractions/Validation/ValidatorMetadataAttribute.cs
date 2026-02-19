using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Validation;

/// <summary>
/// Declarative metadata for an <see cref="Interfaces.IValidator"/> implementation.
/// Allows validators to be self-describing so the rule engine, risk scorer,
/// and reporting layers can discover their characteristics via reflection
/// without coupling to concrete types.
/// </summary>
/// <remarks>
/// Attribute is optional — validators without it continue to work unchanged.
/// Use <see cref="ValidatorMetadataExtensions"/> to retrieve metadata safely
/// with built-in fallback defaults.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ValidatorMetadataAttribute : Attribute
{
    /// <summary>
    /// Stable, unique identifier for the validator (e.g. "DOI_FORMAT").
    /// Used for configuration overrides and audit trails.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The <see cref="FindingKind"/> value this validator primarily produces.
    /// Maps to the well-known constants in <see cref="FindingKind"/>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Default weight used when computing risk scores.
    /// Can be overridden by policy at runtime.
    /// </summary>
    public double DefaultWeight { get; init; } = 1.0;

    /// <summary>
    /// Default severity level for findings produced by this validator.
    /// </summary>
    public Severity DefaultSeverity { get; init; } = Severity.Medium;

    /// <summary>
    /// Human-readable description of what this validator checks.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}
