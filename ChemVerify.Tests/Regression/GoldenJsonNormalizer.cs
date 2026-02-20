using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ChemVerify.Tests.Regression;

/// <summary>
/// Normalizes a JSON response string by replacing dynamic fields (GUIDs, timestamps,
/// hashes, engine versions) with deterministic placeholders, enabling golden-file
/// comparison that ignores harmless run-to-run variation while catching any structural
/// or content drift.
/// </summary>
/// <remarks>
/// <b>Referential integrity</b> is preserved for GUIDs: the first unique GUID
/// encountered is mapped to <c>00000000-0000-0000-0000-000000000001</c>, the second
/// to <c>…000000000002</c>, and so on.  A finding's <c>claimId</c> will still
/// reference the corresponding claim's <c>id</c> after normalization.
/// </remarks>
public static partial class GoldenJsonNormalizer
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"""(createdUtc|generatedUtc)""\s*:\s*""[^""]*""")]
    private static partial Regex TimestampPropertyPattern();

    [GeneratedRegex(@"""(currentHash|artifactHash)""\s*:\s*""[^""]*""")]
    private static partial Regex HashPropertyPattern();

    [GeneratedRegex(@"""engineVersion""\s*:\s*""[^""]*""")]
    private static partial Regex EngineVersionPropertyPattern();

    /// <summary>
    /// Normalizes <paramref name="json"/> by replacing all dynamic values with
    /// stable, indexed placeholders.  The output is re-indented for readability.
    /// </summary>
    public static string Normalize(string json)
    {
        // Step 1: Parse and re-serialize with consistent indentation
        JsonNode? node = JsonNode.Parse(json);
        json = node!.ToJsonString(IndentedOptions);

        // Step 2: Replace every GUID with a sequential placeholder
        //         (preserves referential integrity across the document)
        var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int guidCounter = 0;
        json = GuidPattern().Replace(json, match =>
        {
            string original = match.Value;
            if (!guidMap.TryGetValue(original, out string? replacement))
            {
                guidCounter++;
                replacement = $"00000000-0000-0000-0000-{guidCounter:D12}";
                guidMap[original] = replacement;
            }
            return replacement;
        });

        // Step 3: Normalize timestamp property values
        json = TimestampPropertyPattern().Replace(json,
            m => $"\"{m.Groups[1].Value}\": \"2000-01-01T00:00:00+00:00\"");

        // Step 4: Normalize hash property values
        json = HashPropertyPattern().Replace(json,
            m => $"\"{m.Groups[1].Value}\": \"NORMALIZED\"");

        // Step 5: Normalize engine version
        json = EngineVersionPropertyPattern().Replace(json,
            "\"engineVersion\": \"NORMALIZED\"");

        return json;
    }
}
