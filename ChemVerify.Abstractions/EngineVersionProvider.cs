using System.Reflection;

namespace ChemVerify.Abstractions;

/// <summary>
/// Single source of truth for the engine/rulepack version string.
/// Used in hash computation, artifact provenance, and report rendering.
/// </summary>
public static class EngineVersionProvider
{
    public const string Version = "ChemVerify-v0.2";

    /// <summary>
    /// Version identifier for the validator rule set.
    /// Bumped when validator logic changes in a way that affects findings.
    /// </summary>
    public const string RuleSetVersion = "0.2.0";

    /// <summary>
    /// Returns the assembly informational version of the Abstractions assembly.
    /// Falls back to <see cref="Version"/> when the attribute is absent.
    /// The result is deterministic — it depends only on build-time metadata.
    /// </summary>
    public static string GetAssemblyVersion()
    {
        string? informational = typeof(EngineVersionProvider)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informational) ? Version : informational;
    }
}
