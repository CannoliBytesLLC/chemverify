namespace ChemVerify.Core.Chemistry;

/// <summary>
/// Lightweight, deterministic reference metadata for common solvents and
/// reagents used by the Procedure State Engine and contextual validators.
/// Intentionally small &amp; in-memory — not a chemistry database. Extend by
/// adding entries here; do not introduce I/O.
/// </summary>
public static class ChemistryReference
{
    /// <summary>
    /// Approximate atmospheric-pressure boiling points (°C) keyed by the
    /// canonical entity-key emitted by <c>ReagentRoleExtractor</c> (lowercase
    /// of the matched token). Multiple keys may map to the same physical solvent.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> SolventBoilingPointsCelsius =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["water"] = 100.0,
            ["h2o"] = 100.0,
            ["brine"] = 103.0,
            ["methanol"] = 64.7,
            ["meoh"] = 64.7,
            ["ethanol"] = 78.4,
            ["etoh"] = 78.4,
            ["isopropanol"] = 82.6,
            ["iproh"] = 82.6,
            ["acetone"] = 56.1,
            ["acetonitrile"] = 81.6,
            ["mecn"] = 81.6,
            ["thf"] = 66.0,
            ["tetrahydrofuran"] = 66.0,
            ["dichloromethane"] = 39.6,
            ["dcm"] = 39.6,
            ["ch2cl2"] = 39.6,
            ["chloroform"] = 61.2,
            ["chcl3"] = 61.2,
            ["toluene"] = 110.6,
            ["benzene"] = 80.1,
            ["hexane"] = 68.7,
            ["hexanes"] = 68.7,
            ["heptane"] = 98.4,
            ["pentane"] = 36.1,
            ["diethyl ether"] = 34.6,
            ["et2o"] = 34.6,
            ["ether"] = 34.6,
            ["mtbe"] = 55.2,
            ["dioxane"] = 101.1,
            ["1,4-dioxane"] = 101.1,
            ["dme"] = 85.0,
            ["dimethoxyethane"] = 85.0,
            ["dmf"] = 153.0,
            ["dimethylformamide"] = 153.0,
            ["dmso"] = 189.0,
            ["dimethyl sulfoxide"] = 189.0,
            ["nmp"] = 202.0,
            ["ethyl acetate"] = 77.1,
            ["etoac"] = 77.1
        };

    /// <summary>
    /// Solvents that are protic (donate labile protons) and incompatible with
    /// strongly basic / moisture-sensitive reagents.
    /// </summary>
    public static readonly IReadOnlySet<string> ProticSolvents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "water", "h2o", "brine",
            "methanol", "meoh",
            "ethanol", "etoh",
            "isopropanol", "iproh"
        };

    /// <summary>
    /// Aqueous tokens — used to detect dry/water semantic contradictions.
    /// </summary>
    public static readonly IReadOnlySet<string> AqueousTokens =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "water", "h2o", "brine", "aqueous"
        };

    /// <summary>
    /// Reagents that require strict inert/dry conditions. Keys correspond to
    /// the lowercased token forms emitted by <c>ReagentRoleExtractor</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> InertSensitiveReagents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nah", "sodium hydride",
            "lialh4", "lah",
            "nabh4",
            "n-buli", "buli", "t-buli", "s-buli",
            "lda", "lihmds", "nahmds", "khmds",
            "dibal", "dibal-h",
            "grignard", "mgbr", "mgcl",
            "naome", "naoet", "kotbu"
        };

    /// <summary>
    /// Returns the boiling point in °C for the given solvent entity key,
    /// or <c>null</c> if unknown.
    /// </summary>
    public static double? GetBoilingPointCelsius(string? entityKey)
    {
        if (string.IsNullOrWhiteSpace(entityKey)) return null;
        return SolventBoilingPointsCelsius.TryGetValue(entityKey, out double bp) ? bp : null;
    }

    /// <summary>True for protic solvents (water, alcohols).</summary>
    public static bool IsProticSolvent(string? entityKey) =>
        entityKey is not null && ProticSolvents.Contains(entityKey);

    /// <summary>True for aqueous tokens.</summary>
    public static bool IsAqueous(string? entityKey) =>
        entityKey is not null && AqueousTokens.Contains(entityKey);

    /// <summary>True for reagents that demand strictly inert/dry conditions.</summary>
    public static bool IsInertSensitive(string? entityKey) =>
        entityKey is not null && InertSensitiveReagents.Contains(entityKey);
}
