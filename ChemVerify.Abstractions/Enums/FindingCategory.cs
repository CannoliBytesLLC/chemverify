namespace ChemVerify.Abstractions.Enums;

/// <summary>
/// Distinguishes user-facing findings from internal diagnostic observations.
/// <list type="bullet">
///   <item><see cref="Finding"/> — affects reports and risk scoring; shown to users.</item>
///   <item><see cref="Diagnostic"/> — internal observation explaining why validation
///         was skipped or scoped out; visible only in audit/debug mode.</item>
/// </list>
/// </summary>
public enum FindingCategory
{
    /// <summary>User-facing finding that contributes to risk scoring and reports.</summary>
    Finding,

    /// <summary>Internal diagnostic — explains why a check was not possible or was scoped out.</summary>
    Diagnostic
}
