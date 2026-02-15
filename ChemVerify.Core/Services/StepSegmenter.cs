using System.Text.RegularExpressions;

namespace ChemVerify.Core.Services;

/// <summary>
/// Segments scientific text into logical "steps" for scoped comparison.
/// A step boundary is a sentence terminator, semicolon, newline,
/// bullet prefix, or a transitional phrase ("then", "after", "subsequently", "next", "finally").
/// </summary>
public static class StepSegmenter
{
    // Transitional phrases that mark a new procedural step
    private static readonly Regex BoundaryRegex = new(
        @"(?<=[.;])\s+"
        + @"|(?:\r?\n)+"
        + @"|(?:^|\s)(?:\d+[.)]\s|[-•]\s)"
        + @"|(?<=\S)\s+(?:(?:[Tt]hen|[Aa]fter(?:ward)?s?|[Ss]ubsequently|[Nn]ext|[Ff]inally)\b[,]?\s)",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns a list of (stepIndex, startOffset, endOffset) tuples that cover the
    /// entire text.  Each tuple identifies a contiguous step span.
    /// </summary>
    public static IReadOnlyList<TextStep> Segment(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<TextStep> steps = [];
        MatchCollection matches = BoundaryRegex.Matches(text);

        int stepStart = 0;
        int stepIndex = 0;

        foreach (Match m in matches)
        {
            // The step runs from stepStart to the start of this boundary
            if (m.Index > stepStart)
            {
                steps.Add(new TextStep(stepIndex, stepStart, m.Index));
                stepIndex++;
            }
            stepStart = m.Index + m.Length;
        }

        // Last step: from last boundary end to end-of-text
        if (stepStart < text.Length)
        {
            steps.Add(new TextStep(stepIndex, stepStart, text.Length));
        }

        return steps;
    }

    /// <summary>
    /// Returns the step index for a given character offset, or null if the
    /// offset falls outside any step (e.g. inside a boundary).
    /// </summary>
    public static int? GetStepIndex(IReadOnlyList<TextStep> steps, int charOffset)
    {
        foreach (TextStep step in steps)
        {
            if (charOffset >= step.StartOffset && charOffset < step.EndOffset)
                return step.Index;
        }
        return null;
    }
}

/// <summary>
/// A contiguous step span within the analyzed text.
/// </summary>
public readonly record struct TextStep(int Index, int StartOffset, int EndOffset);
