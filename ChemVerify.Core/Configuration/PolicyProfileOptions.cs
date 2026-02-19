namespace ChemVerify.Core.Configuration;

/// <summary>
/// Strongly-typed options for externally configured policy profiles.
/// Bound from the <c>ChemVerify</c> configuration section.
/// </summary>
/// <remarks>
/// When the configuration section is absent or empty, the resolver falls back
/// to its built-in profile definitions — existing behavior is preserved.
/// </remarks>
public class PolicyProfileOptions
{
    /// <summary>Configuration section name used for binding.</summary>
    public const string SectionName = "ChemVerify";

    /// <summary>
    /// Named policy profiles keyed by profile name (e.g. "StrictChemistryV0").
    /// Profiles defined here take precedence over built-in defaults.
    /// </summary>
    public Dictionary<string, PolicyProfileDefinition> PolicyProfiles { get; set; } = new();
}
