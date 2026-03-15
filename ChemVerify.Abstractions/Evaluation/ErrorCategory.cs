namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// Taxonomy of root causes for incorrect validator findings.
/// Assigned during manual review to classify why a finding was wrong,
/// enabling targeted rule improvements.
/// </summary>
public enum ErrorCategory
{
    /// <summary>Not yet classified by a reviewer.</summary>
    Unclassified,

    /// <summary>Claim extractor failed to parse the text correctly.</summary>
    ExtractionFailure,

    /// <summary>Value normalization (units, magnitude) produced wrong results.</summary>
    NormalizationFailure,

    /// <summary>Claim was linked to the wrong chemical entity or step context.</summary>
    WrongContextLinkage,

    /// <summary>The chemistry rule itself is incorrect or overly broad.</summary>
    IncorrectChemistryRule,

    /// <summary>Validator lacks procedural state tracking (e.g. workup vs reaction phase).</summary>
    MissingProceduralStateTracking,

    /// <summary>Source text is genuinely ambiguous — reasonable people would disagree.</summary>
    AmbiguousWording,

    /// <summary>Literature shorthand that the parser does not recognize (e.g. "rt" for room temperature).</summary>
    LiteratureShorthand,

    /// <summary>Valid but unusual chemistry that violates common heuristics.</summary>
    ValidButUnusualChemistry
}
