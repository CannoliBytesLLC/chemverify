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
        bool hasLabActionVerbs = LabActionVerbRegex.IsMatch(text);

        // Detect references section start
        int? referencesStartOffset = null;
        Match refMatch = ReferencesSectionRegex.Match(text);
        if (refMatch.Success)
            referencesStartOffset = refMatch.Index;

        bool isProcedural = stepCount >= 4 || hasLabActionVerbs;

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
