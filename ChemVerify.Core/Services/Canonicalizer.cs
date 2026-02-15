using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ChemVerify.Abstractions.Interfaces;

namespace ChemVerify.Core.Services;

public class Canonicalizer : ICanonicalizer
{
    private static readonly JsonSerializerOptions StableJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Canonicalize(string input)
    {
        // Normalize line endings to \n, collapse trailing whitespace per line
        string normalized = input.ReplaceLineEndings("\n");
        normalized = Regex.Replace(normalized, @"[ \t]+\n", "\n");
        normalized = normalized.TrimEnd();
        return normalized;
    }

    public string CanonicalizeJson(object value)
    {
        return JsonSerializer.Serialize(value, StableJsonOptions);
    }
}

