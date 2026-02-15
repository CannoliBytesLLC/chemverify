namespace ChemVerify.Abstractions;

/// <summary>
/// Single source of truth for the engine/rulepack version string.
/// Used in hash computation, artifact provenance, and report rendering.
/// </summary>
public static class EngineVersionProvider
{
    public const string Version = "ChemVerify-v0.2";
}
