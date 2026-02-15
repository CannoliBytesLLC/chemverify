namespace ChemVerify.Core.Validation;

/// <summary>
/// Parses "AnalyzedText:start-end" locator strings into offset pairs.
/// </summary>
public static class EvidenceLocator
{
    private const string Prefix = "AnalyzedText:";

    /// <summary>
    /// Attempts to parse a locator like "AnalyzedText:42-55" into start/end offsets.
    /// Returns false if the format is invalid.
    /// </summary>
    public static bool TryParse(string? locator, out int start, out int end)
    {
        start = 0;
        end = 0;

        if (locator is null || !locator.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        string span = locator[Prefix.Length..];
        int dash = span.IndexOf('-');
        if (dash < 0)
            return false;

        return int.TryParse(span[..dash], out start)
            && int.TryParse(span[(dash + 1)..], out end)
            && start >= 0
            && end >= start;
    }

    /// <summary>
    /// Extracts a snippet of +/- <paramref name="radius"/> characters around the
    /// evidence span, clamped to the text bounds. Never throws.
    /// </summary>
    public static string? ExtractSnippet(string? text, int start, int end, int radius = 30)
    {
        if (text is null || text.Length == 0 || start < 0 || end < start)
            return null;

        int clampedStart = Math.Max(0, start - radius);
        int clampedEnd = Math.Min(text.Length, end + radius);

        if (clampedStart >= text.Length)
            return null;

        string snippet = text[clampedStart..clampedEnd];

        string prefix = clampedStart > 0 ? "…" : "";
        string suffix = clampedEnd < text.Length ? "…" : "";

        return $"{prefix}{snippet}{suffix}";
    }
}
