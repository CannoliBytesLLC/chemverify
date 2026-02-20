using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using ChemVerify.API.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChemVerify.Tests.Regression;

/// <summary>
/// Golden regression tests that lock the deterministic output of the ChemVerify
/// verification pipeline.  Each test sends a fixed input text to the <c>/verify</c>
/// endpoint and compares the normalized JSON response to a checked-in golden file.
/// </summary>
/// <remarks>
/// <para><b>How to update golden files</b></para>
/// <para>
/// When a deliberate validator or scoring change alters the expected output,
/// regenerate the golden baselines:
/// </para>
/// <code>
///   # PowerShell
///   $env:CHEMVERIFY_UPDATE_GOLDENS = "true"
///   dotnet test --filter "GoldenRegressionTests"
///   Remove-Item Env:CHEMVERIFY_UPDATE_GOLDENS
///
///   # Bash / CI
///   CHEMVERIFY_UPDATE_GOLDENS=true dotnet test --filter "GoldenRegressionTests"
/// </code>
/// <para>
/// Review the git diff of <c>TestData/Golden/*.golden.json</c> carefully before
/// committing.  If a golden file does not yet exist the first run creates it and
/// <b>fails</b> so you can inspect and commit the baseline.
/// </para>
/// </remarks>
public class GoldenRegressionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    /// <summary>
    /// When <c>true</c>, tests overwrite golden files with current output instead of
    /// asserting equality.  Controlled by <c>CHEMVERIFY_UPDATE_GOLDENS</c>.
    /// </summary>
    private static bool UpdateGoldens =>
        string.Equals(
            Environment.GetEnvironmentVariable("CHEMVERIFY_UPDATE_GOLDENS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public GoldenRegressionTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    /// <summary>
    /// Well-specified Suzuki coupling with explicit reagents, solvent (THF),
    /// temperature (80 °C), quench step, and a valid DOI.
    /// Expected severity: <b>Low</b>.
    /// </summary>
    [Fact]
    public async Task Golden_CleanProcedure_LowRisk()
        => await RunGoldenTest("CleanProcedure");

    /// <summary>
    /// Two distinct reaction routes with divergent temperatures (78 °C vs −78 °C)
    /// and explicit "alternative route" language, triggering multi-scenario detection.
    /// </summary>
    [Fact]
    public async Task Golden_MixedScenario_MediumRisk()
        => await RunGoldenTest("MixedScenario");

    /// <summary>
    /// Contradictory yields (82 % vs 15 %), standalone °C without a numeric value,
    /// and missing procedural details (solvent, temperature, quench).
    /// </summary>
    [Fact]
    public async Task Golden_MalformedUnderspecified_HighRisk()
        => await RunGoldenTest("MalformedUnderspecified");

    // ── Test runner ──────────────────────────────────────────────────────

    private async Task RunGoldenTest(
        string fixtureName,
        [CallerFilePath] string callerPath = "")
    {
        string inputPath = GetTestDataPath("Input", $"{fixtureName}.txt", callerPath);
        string goldenPath = GetTestDataPath("Golden", $"{fixtureName}.golden.json", callerPath);

        Assert.True(File.Exists(inputPath), $"Input fixture not found: {inputPath}");
        string inputText = await File.ReadAllTextAsync(inputPath);

        // ── Hit the public /verify endpoint ──────────────────────────────
        var request = new VerifyTextRequest
        {
            TextToVerify = inputText,
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string rawJson = await response.Content.ReadAsStringAsync();
        string normalized = GoldenJsonNormalizer.Normalize(rawJson);

        // ── Update mode: overwrite golden and pass ───────────────────────
        if (UpdateGoldens)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            await File.WriteAllTextAsync(goldenPath, normalized);
            return;
        }

        // ── First run: create golden and fail for review ─────────────────
        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            await File.WriteAllTextAsync(goldenPath, normalized);
            Assert.Fail(
                $"Golden file created at {goldenPath}. " +
                "Review the output and commit the file to lock this baseline.");
        }

        // ── Normal run: compare normalized output to golden ──────────────
        string expected = await File.ReadAllTextAsync(goldenPath);
        Assert.Equal(expected, normalized);
    }

    // ── Path helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a path under <c>TestData/</c> relative to the source-tree location
    /// of this test file, so tests work regardless of the build output directory.
    /// </summary>
    private static string GetTestDataPath(
        string folder,
        string fileName,
        string callerPath)
    {
        // callerPath → .../ChemVerify.Tests/Regression/GoldenRegressionTests.cs
        string testsProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(callerPath)!)!;
        return Path.Combine(testsProjectDir, "TestData", folder, fileName);
    }
}
