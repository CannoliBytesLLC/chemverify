using System.Text.RegularExpressions;

namespace ChemVerify.Core.Services;

/// <summary>
/// Classifies each <see cref="TextStep"/> into a role so that validators can
/// ignore non-procedural text (questions, headings, references) and avoid
/// false positives.
/// </summary>
public static class StepRoleClassifier
{
    private static readonly Regex LabActionVerbRegex = new(
        @"\b(added|stirred|quenched|extracted|washed|dried|filtered|concentrated|"
        + @"purified|refluxed|cooled|warmed|heated|dissolved|evaporated|decanted|"
        + @"cannulated|sonicated|centrifuged|distilled|recrystallized|precipitated|"
        + @"titrated|degassed|charged|transferred|poured|diluted|"
        + @"collected|neutralized|acidified|adjusted|triturated|filtration|acidification)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MeasuredQuantityRegex = new(
        @"\d+(?:\.\d+)?\s*(?:%|°?C|M|h|min|mg|mL|g|L|K|mol|mmol|kPa|atm|ppm|equiv)",
        RegexOptions.Compiled);

    private static readonly Regex SuggestiveRegex = new(
        @"\b(would\s+you|perhaps|should\s+I|could\s+you|do\s+you\s+want|shall\s+we|may\s+I|how\s+about|why\s+not|what\s+if)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HeadingRegex = new(
        @"^\s*(?:#{1,6}\s|Step\s+\d+[:.]\s*|Procedure[:.]\s*)",
        RegexOptions.Compiled);

    // Matches URLs so we can strip them before checking for '?' question marks
    private static readonly Regex UrlRegex = new(
        @"https?://\S+",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns a mapping from step index to its classified role.
    /// </summary>
    public static IReadOnlyDictionary<int, StepRole> Classify(
        string text,
        IReadOnlyList<TextStep> steps,
        int? referencesStartOffset)
    {
        Dictionary<int, StepRole> roles = new();

        foreach (TextStep step in steps)
        {
            string stepText = text[step.StartOffset..step.EndOffset];
            roles[step.Index] = ClassifyStep(stepText, step.StartOffset, referencesStartOffset);
        }

        return roles;
    }

    private static StepRole ClassifyStep(string stepText, int stepStart, int? referencesStartOffset)
    {
        string trimmed = stepText.Trim();

        if (trimmed.Length < 80 && HeadingRegex.IsMatch(trimmed))
            return StepRole.Header;

        bool hasLabVerbs = LabActionVerbRegex.IsMatch(stepText);
        bool hasMeasured = MeasuredQuantityRegex.IsMatch(stepText);

        // Strip URLs before checking for '?' so that ?q= in Google search URLs
        // does not trigger question classification.
        string textWithoutUrls = UrlRegex.Replace(stepText, "");
        bool hasQuestion = textWithoutUrls.Contains('?');
        bool hasSuggestive = SuggestiveRegex.IsMatch(stepText);

        // Strong question/prompt: suggestive language + "?" + no lab verbs
        // This fires even inside reference zones (trailing "Would you like..." after references)
        if (hasQuestion && hasSuggestive && !hasLabVerbs)
            return StepRole.QuestionOrPrompt;

        // Reference zone: after detecting trailing questions above,
        // everything else past the references boundary is Reference
        if (referencesStartOffset.HasValue && stepStart >= referencesStartOffset.Value)
            return StepRole.Reference;

        // Weak question: "?" with no procedural content at all
        if (hasQuestion && !hasLabVerbs && !hasMeasured)
            return StepRole.QuestionOrPrompt;

        if (hasLabVerbs || hasMeasured)
            return StepRole.Procedure;

        return StepRole.Narrative;
    }
}

/// <summary>
/// The role of a step within the analyzed text.
/// </summary>
public enum StepRole
{
    Procedure,
    Narrative,
    QuestionOrPrompt,
    Reference,
    Header
}
