using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChemVerify.Abstractions.Models;
using ChemVerify.API.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChemVerify.Tests;

/// <summary>
/// Fixture-driven invariant tests. Each of the 150 fixtures in the corpus
/// is run through the ChemVerify engine via the <c>/verify</c> endpoint,
/// and its <c>SuggestedExpectations</c> are evaluated by the DSL mapper
/// in <see cref="ExpectationAsserts"/>.
/// </summary>
public class FixtureInvariantTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Lazy-loaded fixture cache, keyed by Id
    private static readonly Lazy<IReadOnlyDictionary<string, Fixture>> FixtureCache = new(LoadFixtures);

    public FixtureInvariantTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ── MemberData source ───────────────────────────────────────────────

    /// <summary>
    /// Enumerates all fixture IDs, categories, and titles for the test runner.
    /// Each fixture becomes a separate test case.
    /// </summary>
    public static IEnumerable<object[]> AllFixtures()
    {
        foreach (Fixture f in FixtureCache.Value.Values)
            yield return [f.Id, f.Category, f.Title];
    }

    // ── Test method ─────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Fixture_Invariants(string fixtureId, string category, string title)
    {
        Fixture fixture = FixtureCache.Value[fixtureId];

        // ── Run the engine ──────────────────────────────────────────────
        var request = new VerifyTextRequest
        {
            TextToVerify = fixture.Text,
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            FormatFailure(fixture, $"HTTP {response.StatusCode} from /verify"));

        CreateRunResponse? result = await response.Content
            .ReadFromJsonAsync<CreateRunResponse>(JsonOptions);

        Assert.True(result is not null, FormatFailure(fixture, "Null response from /verify"));

        IReadOnlyList<ExtractedClaim> claims = result!.Artifact.Claims;
        IReadOnlyList<ValidationFinding> findings = result.Artifact.Findings;

        // ── Evaluate each expectation ───────────────────────────────────
        List<string> failures = [];
        List<string> unmapped = [];
        int mapped = 0;

        foreach (string expectation in fixture.Expectations)
        {
            IReadOnlyList<ExpectationAsserts.AssertionOutcome> outcomes =
                ExpectationAsserts.Evaluate(expectation, claims, findings);

            foreach (var outcome in outcomes)
            {
                if (!outcome.Mapped)
                {
                    unmapped.Add(outcome.Description);
                    continue;
                }

                mapped++;

                if (!outcome.Passed)
                {
                    failures.Add($"  FAIL: {expectation}\n        → {outcome.Description}");
                }
            }
        }

        // ── Report unmapped expectations (informational, not a failure) ─
        if (unmapped.Count > 0)
        {
            // Output as test message for visibility; does not fail the test
            string unmappedMsg = string.Join("\n  ", unmapped);
            // Use ITestOutputHelper if available; fallback to no-op
            // Unmapped expectations are silently skipped per spec.
        }

        // ── Assert all mapped expectations passed ───────────────────────
        if (failures.Count > 0)
        {
            StringBuilder sb = new();
            sb.AppendLine($"Fixture {fixture.Id} [{fixture.Category}] \"{fixture.Title}\"");
            sb.AppendLine($"  Mapped: {mapped} | Unmapped: {unmapped.Count} | Failed: {failures.Count}");
            sb.AppendLine();

            foreach (string f in failures)
                sb.AppendLine(f);

            sb.AppendLine();
            sb.AppendLine("── Engine Summary ──");
            sb.AppendLine($"  Severity: {result.Report.Severity}");
            sb.AppendLine($"  Risk Score: {result.RiskScore:F3}");
            sb.AppendLine($"  Claims: {claims.Count}");
            sb.AppendLine($"  Findings: {findings.Count}");
            if (result.Report.Attention.Count > 0)
            {
                sb.AppendLine("  Attention items:");
                foreach (string a in result.Report.Attention)
                    sb.AppendLine($"    - {a}");
            }

            Assert.Fail(sb.ToString());
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string FormatFailure(Fixture fixture, string message)
        => $"[{fixture.Id}] [{fixture.Category}] \"{fixture.Title}\": {message}";

    private static IReadOnlyDictionary<string, Fixture> LoadFixtures()
    {
        string path = FixtureParser.GetCorpusPath();

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Fixture corpus not found at {path}. " +
                "Ensure FixtureCorpus.txt is in TestData/Fixtures/.");

        IReadOnlyList<Fixture> fixtures = FixtureParser.ParseFile(path);
        return fixtures.ToDictionary(f => f.Id, StringComparer.Ordinal);
    }
}
