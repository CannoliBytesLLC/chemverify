using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;

namespace ChemVerify.Abstractions.Validation;

/// <summary>
/// Extension methods for reading <see cref="ValidatorMetadataAttribute"/>
/// from <see cref="IValidator"/> instances and types.
/// All methods are null-safe and return sensible defaults when the attribute is absent.
/// </summary>
public static class ValidatorMetadataExtensions
{
    /// <summary>
    /// Returns the <see cref="ValidatorMetadataAttribute"/> applied to the
    /// validator's concrete type, or <c>null</c> if the attribute is absent.
    /// </summary>
    public static ValidatorMetadataAttribute? GetValidatorMetadata(this IValidator validator) =>
        validator.GetType().GetValidatorMetadata();

    /// <summary>
    /// Returns the <see cref="ValidatorMetadataAttribute"/> applied to the
    /// given type, or <c>null</c> if the attribute is absent.
    /// </summary>
    public static ValidatorMetadataAttribute? GetValidatorMetadata(this Type type) =>
        (ValidatorMetadataAttribute?)Attribute.GetCustomAttribute(type, typeof(ValidatorMetadataAttribute));

    /// <summary>
    /// Returns the validator Id from the attribute, or falls back to the
    /// type's unqualified name when the attribute is not present.
    /// </summary>
    public static string GetValidatorId(this IValidator validator)
    {
        var meta = validator.GetValidatorMetadata();
        return meta?.Id ?? validator.GetType().Name;
    }

    /// <summary>
    /// Returns the <see cref="FindingKind"/> from the attribute,
    /// or <c>null</c> if no metadata is declared.
    /// </summary>
    public static string? GetValidatorKind(this IValidator validator) =>
        validator.GetValidatorMetadata()?.Kind;

    /// <summary>
    /// Returns the default weight from the attribute, or <paramref name="fallback"/>
    /// when the attribute is absent.
    /// </summary>
    public static double GetDefaultWeight(this IValidator validator, double fallback = 1.0) =>
        validator.GetValidatorMetadata()?.DefaultWeight ?? fallback;

    /// <summary>
    /// Returns the default severity from the attribute, or <paramref name="fallback"/>
    /// when the attribute is absent.
    /// </summary>
    public static Severity GetDefaultSeverity(this IValidator validator, Severity fallback = Severity.Medium) =>
        validator.GetValidatorMetadata()?.DefaultSeverity ?? fallback;

    /// <summary>
    /// Returns the human-readable description from the attribute,
    /// or an empty string when the attribute is absent.
    /// </summary>
    public static string GetDescription(this IValidator validator) =>
        validator.GetValidatorMetadata()?.Description ?? string.Empty;

    /// <summary>
    /// Returns <c>true</c> if the validator's concrete type has a
    /// <see cref="ValidatorMetadataAttribute"/> applied.
    /// </summary>
    public static bool HasValidatorMetadata(this IValidator validator) =>
        validator.GetValidatorMetadata() is not null;
}
