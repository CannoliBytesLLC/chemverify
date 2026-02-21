using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ChemVerify.Tests;

/// <summary>
/// Parses the fixture corpus text file into strongly-typed <see cref="Fixture"/> records.
/// </summary>
internal static class FixtureParser
{
    private static readonly Regex HeaderRegex = new(
        @"^===\s+(?<id>FIXTURE_\d+):\s+(?<title>.+?)\s+===$",
        RegexOptions.Compiled);

    /// <summary>
    /// Resolves the corpus file path relative to the calling source file.
    /// </summary>
    public static string GetCorpusPath([CallerFilePath] string callerPath = "")
    {
        string testProjectDir = Path.GetDirectoryName(callerPath)!;
        return Path.Combine(testProjectDir, "TestData", "Fixtures", "FixtureCorpus.txt");
    }

    /// <summary>
    /// Parses the fixture corpus from the given file path.
    /// </summary>
    public static IReadOnlyList<Fixture> ParseFile(string filePath)
    {
        string[] lines = File.ReadAllLines(filePath);
        return ParseLines(lines);
    }

    internal static IReadOnlyList<Fixture> ParseLines(string[] lines)
    {
        List<Fixture> fixtures = [];
        int i = 0;

        // Skip preamble (lines before first fixture header)
        while (i < lines.Length && !HeaderRegex.IsMatch(lines[i]))
            i++;

        while (i < lines.Length)
        {
            Match header = HeaderRegex.Match(lines[i]);
            if (!header.Success)
            {
                i++;
                continue;
            }

            string id = header.Groups["id"].Value;
            string title = header.Groups["title"].Value.Trim();
            i++;

            // Parse Category line
            string category = string.Empty;
            if (i < lines.Length && lines[i].StartsWith("Category:", StringComparison.OrdinalIgnoreCase))
            {
                category = lines[i]["Category:".Length..].Trim();
                i++;
            }

            // Parse Text: block
            string text = string.Empty;
            if (i < lines.Length && lines[i].StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
            {
                i++; // skip "Text:" line
                List<string> textLines = [];
                while (i < lines.Length
                    && !lines[i].StartsWith("SuggestedExpectations:", StringComparison.OrdinalIgnoreCase)
                    && !HeaderRegex.IsMatch(lines[i]))
                {
                    textLines.Add(lines[i]);
                    i++;
                }

                text = string.Join('\n', textLines).Trim();
            }

            // Parse SuggestedExpectations: block
            List<string> expectations = [];
            if (i < lines.Length && lines[i].StartsWith("SuggestedExpectations:", StringComparison.OrdinalIgnoreCase))
            {
                i++; // skip "SuggestedExpectations:" line
                while (i < lines.Length
                    && !HeaderRegex.IsMatch(lines[i])
                    && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("- "))
                        line = line[2..].Trim();

                    if (!string.IsNullOrEmpty(line))
                        expectations.Add(line);

                    i++;
                }
            }

            // Skip blank lines before next fixture
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                i++;

            fixtures.Add(new Fixture(id, title, category, text, expectations));
        }

        return fixtures;
    }
}

/// <summary>
/// A single test fixture from the corpus.
/// </summary>
internal record Fixture(
    string Id,
    string Title,
    string Category,
    string Text,
    IReadOnlyList<string> Expectations);
