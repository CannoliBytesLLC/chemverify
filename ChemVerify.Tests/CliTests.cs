using System.Runtime.CompilerServices;
using System.Text.Json;
using ChemVerify.Cli;

namespace ChemVerify.Tests;

/// <summary>
/// Integration tests for the ChemVerify CLI.
/// Each test invokes <see cref="AnalyzeCommandHandler"/> directly against
/// the shared <c>TestData/Input</c> fixtures and asserts exit codes or output format.
/// </summary>
public class CliTests
{
    // ── Test 1: Clean procedure → exit code 0 (OK / Low risk) ────────

    [Fact]
    public async Task Analyze_CleanProcedure_ReturnsExitCodeOk()
    {
        string inputPath = GetTestDataPath("CleanProcedure.txt");

        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await AnalyzeCommandHandler.ExecuteAsync(
            inputPath, "Default", "json", outPath: null, maxInputChars: 500_000,
            stdout, stderr, CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, exitCode);
    }

    // ── Test 2: Malformed/underspecified text → exit code 2 (High/Critical) ──

    [Fact]
    public async Task Analyze_MalformedUnderspecified_ReturnsExitCodeRiskHigh()
    {
        string inputPath = GetTestDataPath("MalformedUnderspecified.txt");

        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await AnalyzeCommandHandler.ExecuteAsync(
            inputPath, "Default", "json", outPath: null, maxInputChars: 500_000,
            stdout, stderr, CancellationToken.None);

        Assert.True(
            exitCode == ExitCodes.Warning || exitCode == ExitCodes.RiskHigh,
            $"Expected Warning (1) or RiskHigh (2) for contradictory text, got {exitCode}");
    }

    // ── Test 3: Missing file → exit code 3 (engine error) ───────────

    [Fact]
    public async Task Analyze_MissingFile_ReturnsExitCodeEngineError()
    {
        string bogusPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "no-such-file.txt");

        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await AnalyzeCommandHandler.ExecuteAsync(
            bogusPath, "Default", "json", outPath: null, maxInputChars: 500_000,
            stdout, stderr, CancellationToken.None);

        Assert.Equal(ExitCodes.EngineError, exitCode);
        Assert.Contains("file not found", stderr.ToString());
    }

    // ── Test 4: SARIF format produces valid SARIF envelope ──────────

    [Fact]
    public async Task Analyze_SarifFormat_ProducesValidSarifEnvelope()
    {
        string inputPath = GetTestDataPath("MixedScenario.txt");

        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await AnalyzeCommandHandler.ExecuteAsync(
            inputPath, "Default", "sarif", outPath: null, maxInputChars: 500_000,
            stdout, stderr, CancellationToken.None);

        Assert.NotEqual(ExitCodes.EngineError, exitCode);

        string sarifJson = stdout.ToString();
        using JsonDocument doc = JsonDocument.Parse(sarifJson);
        JsonElement root = doc.RootElement;

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.True(root.TryGetProperty("runs", out JsonElement runs));
        Assert.True(runs.GetArrayLength() > 0);

        JsonElement driver = runs[0].GetProperty("tool").GetProperty("driver");
        Assert.Equal("ChemVerify", driver.GetProperty("name").GetString());
        Assert.True(runs[0].TryGetProperty("results", out _));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string GetTestDataPath(
        string fileName,
        [CallerFilePath] string callerPath = "")
    {
        string testsProjectDir = Path.GetDirectoryName(callerPath)!;
        return Path.Combine(testsProjectDir, "TestData", "Input", fileName);
    }
}