namespace ChemVerify.Abstractions.Enums;

/// <summary>
/// Classifies reagent reactivity to determine whether missing quench/workup is a concern.
/// Used by <c>ReactivityClassifier</c> to gate quench-related validation.
/// </summary>
public enum ReactivityTier
{
    /// <summary>Unknown or unclassified reagent; no quench requirement assumed.</summary>
    Unknown = 0,

    /// <summary>
    /// Benign additives, mild bases, and common auxiliaries that do not
    /// require an explicit quench step (e.g., triethylamine, K₂CO₃, pyridine).
    /// </summary>
    Benign,

    /// <summary>
    /// Moderately reactive reagents that may need workup attention
    /// depending on context (e.g., NaBH₄ in protic solvent).
    /// </summary>
    Moderate,

    /// <summary>
    /// Highly reactive reagents that almost always require an explicit
    /// quench or controlled workup (e.g., n-BuLi, LiAlH₄, NaH, Grignards, boranes).
    /// </summary>
    High
}
