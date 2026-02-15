using ChemVerify.Abstractions.Validation;

namespace ChemVerify.Tests;

public class ValidationFindingPayloadTests
{
    [Fact]
    public void NullJson_ReturnsFalse()
    {
        bool result = ValidationFindingPayload.TryGetExpectedAndExamples(
            null, out string expected, out IReadOnlyList<string> examples);

        Assert.False(result);
        Assert.Empty(expected);
        Assert.Empty(examples);
    }

    [Fact]
    public void EmptyString_ReturnsFalse()
    {
        bool result = ValidationFindingPayload.TryGetExpectedAndExamples(
            "", out string expected, out IReadOnlyList<string> examples);

        Assert.False(result);
        Assert.Empty(expected);
        Assert.Empty(examples);
    }

    [Fact]
    public void InvalidJson_ReturnsFalse_DoesNotThrow()
    {
        bool result = ValidationFindingPayload.TryGetExpectedAndExamples(
            "{not json", out string expected, out IReadOnlyList<string> examples);

        Assert.False(result);
        Assert.Empty(expected);
        Assert.Empty(examples);
    }

    [Fact]
    public void ValidPayload_WithExpectedAndExamples()
    {
        string json = """{"expected":"temperature numeric value","examples":["0 \u00b0C","25 \u00b0C","-78 \u00b0C"],"token":"\u00b0C"}""";

        bool result = ValidationFindingPayload.TryGetExpectedAndExamples(
            json, out string expected, out IReadOnlyList<string> examples);

        Assert.True(result);
        Assert.Equal("temperature numeric value", expected);
        Assert.Equal(3, examples.Count);
        Assert.Contains("0 °C", examples);
        Assert.Contains("25 °C", examples);
        Assert.Contains("-78 °C", examples);
    }

    [Fact]
    public void ExpectedOnly_NoExamplesArray()
    {
        string json = """{"expected":"some value"}""";

        bool result = ValidationFindingPayload.TryGetExpectedAndExamples(
            json, out string expected, out IReadOnlyList<string> examples);

        Assert.True(result);
        Assert.Equal("some value", expected);
        Assert.Empty(examples);
    }

    [Fact]
    public void MissingExpectedKey_ReturnsFalse()
    {
        string json = """{"examples":["a","b"]}""";

        bool result = ValidationFindingPayload.TryGetExpectedAndExamples(
            json, out string expected, out IReadOnlyList<string> examples);

        Assert.False(result);
    }

    [Fact]
    public void ExpectedNotString_ReturnsFalse()
    {
        string json = """{"expected":42,"examples":["a"]}""";

        bool result = ValidationFindingPayload.TryGetExpectedAndExamples(
            json, out string expected, out IReadOnlyList<string> examples);

        Assert.False(result);
    }
}
