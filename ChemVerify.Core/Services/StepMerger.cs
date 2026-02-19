namespace ChemVerify.Core.Services;

/// <summary>
/// Post-segmentation merge pass that combines fragmented reference entries
/// and table rows into single logical steps.  Operates on the raw
/// <see cref="TextStep"/> list produced by <see cref="StepSegmenter"/>.
/// </summary>
public static class StepMerger
{
    /// <summary>
    /// Merges consecutive steps inside a references block so that each
    /// bibliographic entry (bullet) becomes a single step instead of being
    /// split on every period or newline.
    /// </summary>
    public static IReadOnlyList<TextStep> MergeReferenceBlocks(
        string text,
        IReadOnlyList<TextStep> steps,
        int? referencesStartOffset)
    {
        if (steps.Count == 0 || !referencesStartOffset.HasValue)
            return steps;

        int refStart = referencesStartOffset.Value;

        List<TextStep> result = new();
        int newIndex = 0;

        for (int i = 0; i < steps.Count; i++)
        {
            // Steps before the reference boundary pass through unchanged
            if (steps[i].StartOffset < refStart)
            {
                result.Add(new TextStep(newIndex, steps[i].StartOffset, steps[i].EndOffset));
                newIndex++;
                continue;
            }

            // Inside the reference block: merge until next bullet or blank-line-then-bullet
            string stepText = text[steps[i].StartOffset..steps[i].EndOffset].TrimStart();

            // A new reference entry starts with a bullet (*, -, •, or digit.)
            // or a markdown heading (###), or a horizontal rule (---)
            bool isNewEntry = IsBulletStart(stepText)
                           || IsHeadingOrRule(stepText)
                           || IsTrailingQuestion(stepText);

            if (isNewEntry)
            {
                // Start a new merged step; absorb subsequent continuation lines
                int mergedStart = steps[i].StartOffset;
                int mergedEnd = steps[i].EndOffset;

                while (i + 1 < steps.Count
                       && steps[i + 1].StartOffset >= refStart
                       && IsContinuation(text, steps[i + 1]))
                {
                    i++;
                    mergedEnd = steps[i].EndOffset;
                }

                result.Add(new TextStep(newIndex, mergedStart, mergedEnd));
                newIndex++;
            }
            else
            {
                // Standalone continuation that wasn't absorbed (first fragment in ref zone
                // that doesn't start with a bullet) — emit as-is
                result.Add(new TextStep(newIndex, steps[i].StartOffset, steps[i].EndOffset));
                newIndex++;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns true when the step text starts with a bullet or list marker.
    /// </summary>
    private static bool IsBulletStart(string trimmedText)
    {
        if (trimmedText.Length == 0)
            return false;

        char first = trimmedText[0];

        // Markdown bullets: *, -, •
        if (first is '*' or '-' or '\u2022')
            return true;

        // Numbered list: "1." "2)" etc.
        if (char.IsDigit(first))
        {
            for (int j = 1; j < trimmedText.Length && j < 5; j++)
            {
                if (trimmedText[j] is '.' or ')')
                    return true;
                if (!char.IsDigit(trimmedText[j]))
                    break;
            }
        }

        return false;
    }

    private static bool IsHeadingOrRule(string trimmedText)
    {
        return trimmedText.StartsWith('#')
            || (trimmedText.Length >= 3 && trimmedText[..3] == "---");
    }

    private static bool IsTrailingQuestion(string trimmedText)
    {
        return trimmedText.StartsWith("Would you like", StringComparison.OrdinalIgnoreCase)
            || trimmedText.StartsWith("Do you want", StringComparison.OrdinalIgnoreCase)
            || trimmedText.StartsWith("Shall we", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A step is a "continuation" of the previous reference entry if it doesn't
    /// start a new bullet, heading, rule, or trailing question.
    /// </summary>
    private static bool IsContinuation(string text, TextStep step)
    {
        string trimmed = text[step.StartOffset..step.EndOffset].TrimStart();

        if (trimmed.Length == 0)
            return false;

        // If it starts a new entry, it's not a continuation
        if (IsBulletStart(trimmed) || IsHeadingOrRule(trimmed) || IsTrailingQuestion(trimmed))
            return false;

        return true;
    }
}
