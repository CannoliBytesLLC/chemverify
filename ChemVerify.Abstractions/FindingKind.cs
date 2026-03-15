namespace ChemVerify.Abstractions;

/// <summary>
/// Well-known finding kind values used across validators and the risk scorer.
/// Stored as strings (not an enum) to keep ValidationFinding flexible,
/// but centralised here to prevent typo drift.
/// </summary>
public static class FindingKind
{
    /// <summary>Only one claim exists for this context+unit; nothing to compare against.</summary>
    public const string NotCheckable = "NotCheckable";

    /// <summary>Evidence is missing or could not be retrieved (e.g. DOI not resolved).</summary>
    public const string MissingEvidence = "MissingEvidence";

    /// <summary>Claims differ but strong scenario language suggests they describe different conditions.</summary>
    public const string MultiScenario = "MultiScenario";

    /// <summary>Claims differ beyond the contradiction threshold with no mitigating context.</summary>
    public const string Contradiction = "Contradiction";

    /// <summary>Moisture-sensitive reagent detected alongside protic/aqueous conditions.</summary>
    public const string IncompatibleReagentSolvent = "IncompatibleReagentSolvent";

    /// <summary>Procedural language present but no solvent or medium specified.</summary>
    public const string MissingSolvent = "MissingSolvent";

    /// <summary>Temperature-implying language present but no temperature claim extracted.</summary>
    public const string MissingTemperature = "MissingTemperature";

    /// <summary>Numeric claim lacks a contextKey (e.g., bare mass/volume) and cannot be meaningfully compared.</summary>
    public const string NotComparable = "NotComparable";

    /// <summary>Malformed chemical token such as empty parens, standalone unit, or dangling formatting.</summary>
    public const string MalformedChemicalToken = "MalformedChemicalToken";

    /// <summary>Incomplete or unsupported scientific claim (e.g., "e.g." with unit but no number).</summary>
    public const string UnsupportedOrIncompleteClaim = "UnsupportedOrIncompleteClaim";

    /// <summary>Mixed citation styles reduce traceability (DOI + author-year in same document).</summary>
    public const string CitationTraceabilityWeak = "CitationTraceabilityWeak";

    /// <summary>Reactive reagent present but no quench/workup step detected afterward.</summary>
    public const string MissingQuench = "MissingQuench";

    /// <summary>Dry/inert conditions established but aqueous media introduced without explicit workup transition.</summary>
    public const string AmbiguousWorkupTransition = "AmbiguousWorkupTransition";

    /// <summary>Equivalents claim is inconsistent with the mmol values present.</summary>
    public const string EquivInconsistent = "EquivInconsistent";

    /// <summary>Condition values (temp/time) differ across steps or clusters — expected in multistep synthesis, not a contradiction.</summary>
    public const string CrossStepConditionVariation = "CrossStepConditionVariation";

    /// <summary>Placeholder or missing token in text — template artifact, not a chemistry error.</summary>
    public const string PlaceholderOrMissingToken = "PlaceholderOrMissingToken";

    /// <summary>Mass and mmol for a reagent are consistent with a plausible molecular weight.</summary>
    public const string MwConsistent = "MwConsistent";

    /// <summary>Mass and mmol for a reagent imply an implausible molecular weight.</summary>
    public const string MwImplausible = "MwImplausible";

    /// <summary>Reported product mass and yield percentage are inconsistent with starting material.</summary>
    public const string YieldMassInconsistent = "YieldMassInconsistent";

    /// <summary>Percent values describe different property types (e.g. yield vs impurity) and are not comparable.</summary>
    public const string DifferentPercentKind = "DifferentPercentKind";

    /// <summary>Implied MW contradicts the known molecular weight for a common reagent.</summary>
    public const string MwKnownMismatch = "MwKnownMismatch";

    /// <summary>Entity name is ambiguous (e.g. HCl could be free acid or hydrochloride salt).</summary>
    public const string EntityAmbiguous = "EntityAmbiguous";

    /// <summary>Quench/workup may be missing but the procedure appears truncated.</summary>
    public const string PossiblyTruncated = "PossiblyTruncated";

    /// <summary>Time claims represent sequential durations (e.g., "30 min then an additional 15 min"), not a contradiction.</summary>
    public const string SequentialDuration = "SequentialDuration";

    /// <summary>One time is a checkpoint marker and the other is a cumulative total — not contradictory.</summary>
    public const string CheckpointVsTotal = "CheckpointVsTotal";

    /// <summary>Time claims belong to different operations (e.g., stir vs addition) — not contradictory.</summary>
    public const string DifferentOperation = "DifferentOperation";

    /// <summary>Time claims are at different temperature regimes (e.g., 0 °C vs room temperature) — not contradictory.</summary>
    public const string DifferentConditionContext = "DifferentConditionContext";

    /// <summary>Percent values are part of a chromatography gradient (e.g., 2 % → 4 %) — not contradictory.</summary>
    public const string GradientElution = "GradientElution";

    /// <summary>Dry/inert conditions established but aqueous media introduced with clear workup transition language.</summary>
    public const string WorkupTransitionDetected = "WorkupTransitionDetected";

    // ── Diagnostic-kind registry ────────────────────────────────────────
    // Kinds in this set are internal observations that explain why a check
    // was not performed. They should NOT affect user-facing reports or risk
    // scoring. Validators emit these as FindingCategory.Diagnostic.

    /// <summary>
    /// Returns <c>true</c> if the given kind is classified as a diagnostic
    /// (internal observation) rather than a user-facing finding.
    /// </summary>
    public static bool IsDiagnosticKind(string? kind) =>
        kind is not null && DiagnosticKinds.Contains(kind);

    /// <summary>
    /// Well-known kinds that represent diagnostic observations, not actionable findings.
    /// Kinds rendered in user-facing report sections (Attention, NextQuestions) such as
    /// <see cref="MultiScenario"/> and <see cref="PossiblyTruncated"/> must NOT
    /// appear here (they are rendered in user-facing report sections).
    /// </summary>
    public static readonly HashSet<string> DiagnosticKinds = new(StringComparer.Ordinal)
    {
        NotComparable,
        NotCheckable,
        DifferentPercentKind,
        DifferentOperation,
        DifferentConditionContext,
        SequentialDuration,
        CheckpointVsTotal,
        GradientElution,
        EntityAmbiguous,
        CrossStepConditionVariation
    };
}
