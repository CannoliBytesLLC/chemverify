using System.Text.RegularExpressions;

namespace ChemVerify.Core.Services;

/// <summary>
/// Lightweight heuristic detector that determines whether a piece of text
/// is procedural (e.g., an experimental section) or narrative (e.g., a
/// literature review paragraph). Used to gate safety/procedure validators
/// such as <c>QuenchWhenReactiveReagentValidator</c>.
/// </summary>
public static class ProceduralContextDetector
{
    /// <summary>
    /// Imperative / lab-action verbs commonly found in experimental procedures.
    /// </summary>
    private static readonly Regex LabActionVerbRegex = new(
        @"\b(added|stirred|quenched|extracted|washed|dried|filtered|concentrated|"
        + @"purified|refluxed|cooled|warmed|heated|dissolved|evaporated|decanted|"
        + @"cannulated|sonicated|centrifuged|distilled|recrystallized|precipitated|"
        + @"titrated|degassed|charged|transferred|poured|diluted)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Minimum number of distinct lab-action verb matches required to consider
    /// text procedural based on verbs alone. A single verb in a narrative or
    /// review paragraph is not sufficient.
    /// </summary>
    private const int MinLabVerbMatchCount = 2;

    /// <summary>
    /// Numeric quantities (e.g. "10 mmol", "20 mL", "0 °C") that strongly indicate
    /// operational procedural text rather than narrative description.
    /// </summary>
    private static readonly Regex NumericQuantityRegex = new(
        @"\d+(?:\.\d+)?\s*(?:mmol|mol|mg|g|kg|µ?[Ll]|mL|µL|°C|K|°F|min|hr?|equiv|eq|wt%|atm|psi|bar|torr|M\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Review / narrative hedge cues. When these appear near lab-action verbs,
    /// the text is more likely a literature review than a procedure.
    /// </summary>
    private static readonly Regex NarrativeHedgeRegex = new(
        @"\b(reported(?:ly)?|previously|in\s+(?:prior|earlier)\s+work|literature|was\s+shown|"
        + @"has\s+been\s+described|it\s+is\s+known|typically\s+used|commonly\s+employed|"
        + @"well[\s-]established|are\s+widely\s+used|have\s+been\s+reported)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Patterns that mark the start of a references / bibliography section.
    /// Matches headings like "References", "### References", "Bibliography", etc.
    /// </summary>
    private static readonly Regex ReferencesSectionRegex = new(
        @"(?:^|\n)\s*(?:#{1,6}\s+)?(?:References|Bibliography|Works\s+Cited)\s*\n",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Analyze the text and segmented steps to determine procedural context.
    /// </summary>
    public static ProceduralContext Detect(string text, IReadOnlyList<TextStep> steps)
    {
        if (string.IsNullOrEmpty(text))
            return new ProceduralContext(false, 0, false, null);

        int stepCount = steps.Count;
        int labVerbMatchCount = LabActionVerbRegex.Matches(text).Count;
        bool hasLabActionVerbs = labVerbMatchCount >= MinLabVerbMatchCount;
        bool hasNumericQuantities = NumericQuantityRegex.IsMatch(text);
        int hedgeCount = NarrativeHedgeRegex.Matches(text).Count;

        // Detect references section start
        int? referencesStartOffset = null;
        Match refMatch = ReferencesSectionRegex.Match(text);
        if (refMatch.Success)
            referencesStartOffset = refMatch.Index;

        // Narrative hedge dampening: if review/literature cues are at least
        // as frequent as lab verbs, the text is more likely a review or
        // discussion, not an executable procedure. Require a higher bar.
        bool hedgeDampened = hedgeCount > 0 && hedgeCount >= labVerbMatchCount;

        // Text is procedural if it has many steps, OR has sufficient lab-action
        // verbs combined with numeric quantities (temperatures, amounts, etc.).
        // A single verb mention without quantities indicates narrative, not procedure.
        // When hedge cues dampen the signal, require the high-step-count path.
        bool isProcedural;
        if (hedgeDampened)
        {
            isProcedural = stepCount >= 4;
        }
        else
        {
            isProcedural = stepCount >= 4
                || (hasLabActionVerbs && hasNumericQuantities)
                || labVerbMatchCount >= 4;
        }

        return new ProceduralContext(isProcedural, stepCount, hasLabActionVerbs, referencesStartOffset);
    }
}

/// <summary>
/// Result of procedural-context detection for a piece of text.
/// </summary>
public readonly record struct ProceduralContext(
    bool IsProcedural,
    int StepCount,
    bool HasLabActionVerbs,
    int? ReferencesStartOffset);
