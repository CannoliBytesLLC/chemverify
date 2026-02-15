using System.Text.Json;

namespace ChemVerify.Abstractions.Validation;

/// <summary>
/// Safe helpers for reading <see cref="Models.ValidationFinding.JsonPayload"/> values.
/// Every method is null-safe and never throws.
/// </summary>
public static class ValidationFindingPayload
{
    /// <summary>
    /// Attempts to extract the "expected" string and "examples" string list
    /// from a JSON payload.  Returns <c>false</c> on <c>null</c>, empty,
    /// or malformed JSON — never throws.
    /// </summary>
    public static bool TryGetExpectedAndExamples(
        string? json,
        out string expected,
        out IReadOnlyList<string> examples)
    {
        expected = string.Empty;
        examples = [];

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("expected", out JsonElement expectedEl)
                || expectedEl.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            expected = expectedEl.GetString() ?? string.Empty;

            if (root.TryGetProperty("examples", out JsonElement examplesEl)
                && examplesEl.ValueKind == JsonValueKind.Array)
            {
                List<string> list = [];
                foreach (JsonElement item in examplesEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string? s = item.GetString();
                        if (s is not null)
                            list.Add(s);
                    }
                }
                examples = list;
            }

            return expected.Length > 0;
        }
        catch (Exception)
        {
            // Malformed JSON, unexpected structure, etc.
            return false;
        }
    }
}
