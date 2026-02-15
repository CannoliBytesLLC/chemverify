using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.API.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChemVerify.Tests;

public class VerifyTextEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public VerifyTextEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BaselineChemistryParagraph_ReturnsCompletedRun()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The Suzuki-Miyaura coupling was performed in toluene at 80 °C for 12 h, affording the biaryl product in 92% yield. The catalyst loading was 2 mol% Pd(PPh₃)₄.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.RunId);
        Assert.True(result.RiskScore >= 0.0 && result.RiskScore <= 1.0);
        Assert.NotNull(result.Report);
        Assert.NotNull(result.Artifact);
        Assert.Equal("Completed", result.Artifact.Run.Status.ToString());
        Assert.NotNull(result.Artifact.Run.InputText);
        Assert.NotEmpty(result.Artifact.Run.GetAnalyzedText());
    }

    [Fact]
    public async Task TimeEquivalence_2hVs120min_DetectedAsConsistent()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reaction was stirred for 2 h at room temperature. After 120 min, the mixture was quenched with water.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.True(result.Report.Confirmed.Any(c => c.Contains('\u2248') || c.Contains("consistent", StringComparison.OrdinalIgnoreCase)),
            "Expected a confirmed finding showing time equivalence.");
    }

    [Fact]
    public async Task MultiScenarioTemperatures_78CvsNeg78C_WithAlternativeRoute()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reaction was heated to 78 °C for 4 h. In an alternative route, the mixture was cooled to -78 °C before addition of the organolithium reagent.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.True(
            result.Report.Attention.Any(a => a.Contains("regime", StringComparison.OrdinalIgnoreCase) ||
                                             a.Contains("scenario", StringComparison.OrdinalIgnoreCase)),
            "Expected a multi-scenario attention finding for divergent temperatures with 'alternative route' language.");
    }

    [Fact]
    public async Task ContradictoryYields_82And15_FlaggedAsContradiction()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The product was isolated in 82% yield after column chromatography. The overall yield of the process was 15%.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(result.RiskScore > 0.0, "Risk score should be elevated for contradictory yields.");
        Assert.NotNull(result.Report);
        Assert.True(
            result.Report.Attention.Any(a => a.Contains("contradiction", StringComparison.OrdinalIgnoreCase) ||
                                             a.Contains('\u274c')),
            "Expected a contradiction finding for 82% vs 15% yield.");
    }

    [Fact]
    public async Task InvalidDoiFormat_ExtractedAndFlaggedAsFail()
    {
        // The extractor is permissive and will capture 10.1038/NOT#A#DOI as a DOI-like claim.
        // The validator applies the strict DOI regex and should flag it as invalid.
        var request = new VerifyTextRequest
        {
            TextToVerify = "This method was described by Smith et al. (DOI: 10.1038/NOT#A#DOI).",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Artifact);

        bool hasDoiClaim = result.Artifact.Claims.Any(c => c.ClaimType == ClaimType.CitationDoi);
        Assert.True(hasDoiClaim, "A DOI-like claim should be extracted from the text.");

        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "DoiFormatValidator" &&
                f.Status == ValidationStatus.Fail),
            "Expected a Fail finding from DoiFormatValidator for the invalid DOI.");
    }

    [Fact]
    public async Task IncompatibleReagentSolvent_NaHInWater_Flagged()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "NaH (60% dispersion) was added portionwise to water at 0 °C.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(result.RiskScore > 0.0, "Risk score should be elevated for incompatible reagent/solvent.");
        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "IncompatibleReagentSolventValidator" &&
                f.Status == ValidationStatus.Fail),
            "Expected a Fail finding for moisture-sensitive reagent (NaH) in aqueous conditions.");
    }

    [Fact]
    public async Task MissingSolvent_StirredWithoutSolvent_Flagged()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The mixture was stirred for 2 h at room temperature and then filtered.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "MissingSolventValidator" &&
                f.Status == ValidationStatus.Fail),
            "Expected a Fail finding for procedure verbs without any solvent specified.");
    }

    [Fact]
    public async Task MissingTemperature_DropwiseWithoutTemp_Flagged()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reagent was added dropwise over 30 min and the mixture was left to react overnight.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "MissingTemperatureWhenImpliedValidator" &&
                f.Status == ValidationStatus.Fail),
            "Expected a Fail finding for implied temperature control (dropwise) without a temperature claim.");
    }

    [Fact]
    public async Task EngineVersion_IsNotEmpty_AndEqualsExpected()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reaction was carried out in THF at 25 °C for 1 h.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Artifact);
        Assert.False(string.IsNullOrEmpty(result.Artifact.EngineVersion),
            "EngineVersion must not be empty in the artifact.");
        Assert.Equal(EngineVersionProvider.Version, result.Artifact.EngineVersion);
    }

    [Fact]
    public async Task DoiFormat_InvalidCharacters_Fails_ValidDoi_Passes()
    {
        var invalidRequest = new VerifyTextRequest
        {
            TextToVerify = "See DOI: 10.1038/NOT#A#DOI for details.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage invalidResponse = await _client.PostAsJsonAsync("/verify", invalidRequest);
        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);

        CreateRunResponse? invalidResult = await invalidResponse.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(invalidResult);
        Assert.True(
            invalidResult.Artifact.Findings.Any(f =>
                f.ValidatorName == "DoiFormatValidator" &&
                f.Status == ValidationStatus.Fail),
            "Expected Fail for DOI with # characters.");

        var validRequest = new VerifyTextRequest
        {
            TextToVerify = "See DOI: 10.1021/acs.orglett.1c02345 for details.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage validResponse = await _client.PostAsJsonAsync("/verify", validRequest);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        CreateRunResponse? validResult = await validResponse.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(validResult);
        Assert.True(
            validResult.Artifact.Findings.Any(f =>
                f.ValidatorName == "DoiFormatValidator" &&
                f.Status == ValidationStatus.Pass),
            "Expected Pass for valid DOI 10.1021/acs.orglett.1c02345.");
    }

    [Fact]
    public async Task AlternativeRoute_EmitsMultiScenarioOnly_NoFailContradictions()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The product was obtained in 85% yield. In an alternative route, the yield was 22%.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var contradictionFindings = result.Artifact.Findings
            .Where(f => f.ValidatorName == "NumericContradictionValidator")
            .ToList();

        Assert.True(
            contradictionFindings.Any(f => f.Kind == "MultiScenario"),
            "Expected a MultiScenario finding for alternative route language.");
        Assert.False(
            contradictionFindings.Any(f => f.Status == ValidationStatus.Fail && f.Kind == "Contradiction"),
            "Should NOT emit Fail contradictions when multi-scenario language is present.");
    }

    [Fact]
    public async Task SourceLocator_UsesAnalyzedTextPrefix()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reaction was run at 80 °C for 2 h in THF.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var claimsWithLocators = result.Artifact.Claims
            .Where(c => c.SourceLocator is not null)
            .ToList();

        Assert.NotEmpty(claimsWithLocators);
        Assert.All(claimsWithLocators, c =>
            Assert.StartsWith("AnalyzedText:", c.SourceLocator!));
    }

    [Fact]
    public async Task DifferentMassesAndVolumes_NoContradictions()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "Benzaldehyde (1.06 g, 10 mmol) was dissolved in 10 mL of MeOH. NaBH4 (0.38 g, 10 mmol) was added in portions. The mixture was diluted with 20 mL of water and extracted.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var contradictionFindings = result.Artifact.Findings
            .Where(f => f.ValidatorName == "NumericContradictionValidator")
            .ToList();

        Assert.False(
            contradictionFindings.Any(f => f.Status == ValidationStatus.Fail && f.Kind == "Contradiction"),
            "Different reagent masses (1.06 g vs 0.38 g) or volumes (10 mL vs 20 mL) should NOT be flagged as contradictions.");

        // Claims without a comparable contextKey should be marked NotComparable
        Assert.True(
            contradictionFindings.Any(f => f.Kind == "NotComparable"),
            "Bare mass/volume claims should be marked as NotComparable.");
    }

    [Fact]
    public async Task PercentContext_HClAndEluent_NotClassifiedAsYield()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "To a solution of the aldehyde in THF was added 10% HCl (2 mL). "
                         + "The product was isolated in 88% yield. "
                         + "Purification by column chromatography (20% ethyl acetate in hexanes) gave the desired compound.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        // There should be no Fail contradiction between 10% HCl, 88% yield, and 20% EtOAc
        var contradictionFindings = result.Artifact.Findings
            .Where(f => f.ValidatorName == "NumericContradictionValidator"
                     && f.Status == ValidationStatus.Fail
                     && f.Kind == "Contradiction")
            .ToList();

        Assert.Empty(contradictionFindings);

        // The 88% near "yield" should be extracted with contextKey=yield
        var yieldClaims = result.Artifact.Claims
            .Where(c => c.Unit == "%" && c.NormalizedValue == "88"
                     && c.JsonPayload is not null && c.JsonPayload.Contains("\"yield\""))
            .ToList();
        Assert.Single(yieldClaims);

        // The 10% near "HCl" should NOT have contextKey=yield
        var hclClaims = result.Artifact.Claims
            .Where(c => c.Unit == "%" && c.NormalizedValue == "10")
            .ToList();
        Assert.NotEmpty(hclClaims);
        Assert.DoesNotContain(hclClaims, c =>
            c.JsonPayload is not null && c.JsonPayload.Contains("\"yield\""));

        // The 20% near "ethyl acetate" should NOT have contextKey=yield
        var eluentClaims = result.Artifact.Claims
            .Where(c => c.Unit == "%" && c.NormalizedValue == "20")
            .ToList();
        Assert.NotEmpty(eluentClaims);
        Assert.DoesNotContain(eluentClaims, c =>
            c.JsonPayload is not null && c.JsonPayload.Contains("\"yield\""));
    }

    [Fact]
    public async Task TimeClaims_DifferentActions_NoContradiction()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "NaBH4 was added over 5 min. The reaction was maintained at 0 °C for 30 min.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var contradictionFindings = result.Artifact.Findings
            .Where(f => f.ValidatorName == "NumericContradictionValidator"
                     && f.Status == ValidationStatus.Fail
                     && f.Kind == "Contradiction")
            .ToList();

        Assert.Empty(contradictionFindings);
    }

    [Fact]
    public async Task DoiInMarkdownLink_ExtractedCleanly()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "See [https://doi.org/10.3762/bjoc.9.76](https://www.google.com/search?q=https://doi.org/10.3762/bjoc.9.76) for details.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var doiClaims = result.Artifact.Claims
            .Where(c => c.ClaimType == ClaimType.CitationDoi)
            .ToList();

        Assert.NotEmpty(doiClaims);
        Assert.Contains(doiClaims, c => c.NormalizedValue == "10.3762/bjoc.9.76");

        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "DoiFormatValidator" &&
                f.Status == ValidationStatus.Pass),
            "Expected DoiFormatValidator to pass for the cleanly extracted DOI.");
    }

    [Fact]
    public async Task DoiPlainText_StillExtractedCorrectly()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The protocol follows DOI: 10.1021/acs.orglett.1c02345 as described.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var doiClaims = result.Artifact.Claims
            .Where(c => c.ClaimType == ClaimType.CitationDoi)
            .ToList();

        Assert.Single(doiClaims);
        Assert.Equal("10.1021/acs.orglett.1c02345", doiClaims[0].NormalizedValue);

        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "DoiFormatValidator" &&
                f.Status == ValidationStatus.Pass),
            "Expected DoiFormatValidator to pass for plain-text DOI.");
    }

    [Fact]
    public async Task ScientificTextV0_SkipsReagentSafetyValidators()
    {
        // This text would trigger IncompatibleReagentSolvent under StrictChemistryV0
        var request = new VerifyTextRequest
        {
            TextToVerify = "NaH (60% dispersion) was added portionwise to water at 0 °C.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        Assert.False(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "IncompatibleReagentSolventValidator"),
            "ScientificTextV0 should skip IncompatibleReagentSolventValidator.");
        Assert.False(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "MissingSolventValidator"),
            "ScientificTextV0 should skip MissingSolventValidator.");
        Assert.False(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "MissingTemperatureWhenImpliedValidator"),
            "ScientificTextV0 should skip MissingTemperatureWhenImpliedValidator.");
    }

    [Fact]
    public async Task ScientificTextV0_DampensDoiFailRisk()
    {
        // 4 invalid DOIs under StrictChemistryV0 would give high/critical risk.
        // Under ScientificTextV0, the risk should be dampened.
        var request = new VerifyTextRequest
        {
            TextToVerify = "See DOI: 10.1038/NOT#1, DOI: 10.1038/NOT#2, DOI: 10.1038/NOT#3, DOI: 10.1038/NOT#4.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        Assert.True(result.RiskScore < 1.0,
            "Under ScientificTextV0, 4 DOI fails should not produce maximum risk.");
        Assert.NotEqual("Critical", result.Report!.Severity);
    }

    [Fact]
    public async Task MalformedChemicalToken_EmptyParens_Detected()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reaction used benzene () as the solvent at °C for 2 h.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "MalformedChemicalTokenValidator" &&
                f.Kind == "MalformedChemicalToken"),
            "Expected MalformedChemicalTokenValidator to detect empty parentheses or standalone °C.");
    }

    [Fact]
    public async Task MixedCitationStyle_DoiAndAuthorYear_Detected()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "This was reported by (Smith, 2020) and confirmed in DOI: 10.1021/acs.orglett.1c02345.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "MixedCitationStyleValidator" &&
                f.Kind == "CitationTraceabilityWeak"),
            "Expected MixedCitationStyleValidator to detect mixed DOI and author-year citations.");
    }

    [Fact]
    public async Task IncompleteScientificClaim_EgWithoutNumber_Detected()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The solvent was selected based on polarity, e.g. mL of THF were used.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        Assert.True(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "IncompleteScientificClaimValidator" &&
                f.Kind == "UnsupportedOrIncompleteClaim"),
            "Expected IncompleteScientificClaimValidator to detect 'e.g.' followed by unit without number.");
    }

    [Fact]
    public async Task ReportAttention_NoDuplicates_TextIntegrity()
    {
        // Text triggers both MalformedChemicalToken (standalone °C) and
        // UnsupportedOrIncompleteClaim (e.g. mL without number).
        // Each finding should appear exactly once in report.attention.
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reaction used benzene () as the solvent at °C. Polarity was chosen, e.g. mL of THF were used.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);

        // Every attention entry must be unique
        Assert.Equal(result.Report.Attention.Count, result.Report.Attention.Distinct().Count());

        // No attention item should appear with both ❌ and ⚠️ icons for the same message
        var attentionMessages = result.Report.Attention
            .Select(a => a.TrimStart('\u274c', '\u26a0', '\ufe0f', ' '))
            .ToList();
        Assert.Equal(attentionMessages.Count, attentionMessages.Distinct().Count());
    }

    [Fact]
    public async Task ComparativeChainWithAuthorYearCitation_NotFlagged()
    {
        // Comparative chain "A > B" with author-year citation "(Ward & Rhee, 1989)"
        // in the same sentence should NOT be flagged as missing citation.
        var request = new VerifyTextRequest
        {
            TextToVerify = "The selectivity of catalyst A > catalyst B was demonstrated (Ward & Rhee, 1989).",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        Assert.False(
            result.Artifact.Findings.Any(f =>
                f.ValidatorName == "IncompleteScientificClaimValidator" &&
                f.Kind == "UnsupportedOrIncompleteClaim" &&
                f.Message.Contains("Comparative chain")),
            "Comparative chain with author-year citation (Ward & Rhee, 1989) should NOT be flagged.");
    }

    [Fact]
    public async Task MalformedChemicalToken_StandaloneC_IncludesSuggestionPayload()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The mixture was heated at °C for 1 h.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var finding = result.Artifact.Findings.FirstOrDefault(f =>
            f.ValidatorName == "MalformedChemicalTokenValidator" &&
            f.Kind == "MalformedChemicalToken" &&
            f.Message.Contains("°C"));
        Assert.NotNull(finding);
        Assert.NotNull(finding.JsonPayload);

        using var doc = System.Text.Json.JsonDocument.Parse(finding.JsonPayload);
        var root = doc.RootElement;
        Assert.Equal("temperature numeric value", root.GetProperty("expected").GetString());
        Assert.Equal("°C", root.GetProperty("token").GetString());

        var examples = root.GetProperty("examples").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("0 °C", examples);
        Assert.Contains("25 °C", examples);
        Assert.Contains("-78 °C", examples);

        // Report attention should include a suggestion line
        Assert.NotNull(result.Report);
        Assert.True(
            result.Report.Attention.Any(a => a.Contains("Expected:") && a.Contains("temperature")),
            "Report attention should include a suggestion line for standalone °C.");
    }

    [Fact]
    public async Task Verdict_TextIntegrityOnly_RecommendCleanup()
    {
        // Text triggers only text-integrity findings (no contradictions, no chemistry)
        var request = new VerifyTextRequest
        {
            TextToVerify = "The mixture was heated at °C for 1 h in THF.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);

        Assert.Equal(
            "Scientific writing/format issues detected. Manual cleanup recommended.",
            result.Report.Verdict);
    }

    [Fact]
    public async Task SourcePassage_VerifyOnly_ShowsInputText()
    {
        // In VerifyOnly mode, the run has InputText but no Output.
        // The Source Passage (analyzedText) should show the InputText.
        var request = new VerifyTextRequest
        {
            TextToVerify = "The reaction was run at 80 °C.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Artifact.Run.InputText);
        Assert.Null(result.Artifact.Run.Output);
        Assert.Equal(request.TextToVerify, result.Artifact.Run.GetAnalyzedText());
    }

    [Fact]
    public async Task EntityScopedComparison_DifferentReagentMasses_NoContradiction()
    {
        // Benzaldehyde 1.06 g vs NaBH4 0.38 g: different entities, should NOT contradict
        var request = new VerifyTextRequest
        {
            TextToVerify = "Benzaldehyde (1.06 g, 10 mmol) was dissolved in 10 mL of MeOH. NaBH4 (0.38 g, 10 mmol) was added in portions.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var contradictions = result.Artifact.Findings
            .Where(f => f.ValidatorName == "NumericContradictionValidator"
                     && f.Status == ValidationStatus.Fail
                     && f.Kind == "Contradiction")
            .ToList();

        Assert.Empty(contradictions);
    }

    [Fact]
    public async Task EntityScopedComparison_SameReagentDifferentAmounts_FlagsContradiction()
    {
        // Same reagent mentioned twice with contradictory amounts
        var request = new VerifyTextRequest
        {
            TextToVerify = "The product was isolated in 85% yield. The overall yield of the synthesis was 15%.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var contradictions = result.Artifact.Findings
            .Where(f => f.ValidatorName == "NumericContradictionValidator"
                     && f.Status == ValidationStatus.Fail
                     && f.Kind == "Contradiction")
            .ToList();

        Assert.NotEmpty(contradictions);
    }

    [Fact]
    public async Task ClaimHasEntityKeyAndStepIndex()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "Benzaldehyde (1.06 g) was dissolved in MeOH.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var massClaim = result.Artifact.Claims
            .FirstOrDefault(c => c.ClaimType == ClaimType.NumericWithUnit && c.RawText == "1.06 g");
        Assert.NotNull(massClaim);
        Assert.NotNull(massClaim.EntityKey);
        Assert.NotNull(massClaim.StepIndex);
    }

    // ── Quench/Workup Validator ──────────────────────────────────────────

    [Fact]
    public async Task NaH_NoQuench_MissingQuenchFlagged()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "NaH (0.24 g, 10 mmol) was added to a solution of the alcohol (1.0 g, 5 mmol) in DMF. The mixture was stirred at 60 °C for 2 h. The product was concentrated under reduced pressure.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.True(
            result.Report.Attention.Any(a => a.Contains("MISSING_QUENCH", StringComparison.OrdinalIgnoreCase)),
            "Expected CHEM.MISSING_QUENCH finding when NaH is used without quench.");
    }

    [Fact]
    public async Task NaH_WithQuench_NoMissingQuenchFinding()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "NaH (0.24 g, 10 mmol) was added to a solution of the alcohol (1.0 g, 5 mmol) in DMF. The mixture was stirred at 60 °C for 2 h. The reaction was quenched with sat. NH4Cl and extracted with EtOAc.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.DoesNotContain(result.Report.Attention,
            a => a.Contains("MISSING_QUENCH", StringComparison.OrdinalIgnoreCase));
    }

    // ── Dry/Inert Mismatch Validator ─────────────────────────────────────

    [Fact]
    public async Task AnhydrousUnderN2_ThenBrine_WithoutWorkup_MismatchWarned()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "Anhydrous THF was used under N2 atmosphere. The substrate (1.0 g, 5 mmol) was dissolved. The mixture was diluted with brine and the layers were collected.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.True(
            result.Report.Attention.Any(a => a.Contains("AMBIGUOUS_WORKUP_TRANSITION", StringComparison.OrdinalIgnoreCase)),
            "Expected CHEM.AMBIGUOUS_WORKUP_TRANSITION when dry/inert followed by brine without workup language.");
    }

    [Fact]
    public async Task AnhydrousUnderN2_ThenWashedWithBrine_NoMismatch()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "Anhydrous THF was used under N2 atmosphere. The substrate (1.0 g, 5 mmol) was dissolved. The reaction was quenched and washed with brine.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.DoesNotContain(result.Report.Attention,
            a => a.Contains("AMBIGUOUS_WORKUP_TRANSITION", StringComparison.OrdinalIgnoreCase));
    }

    // ── Equivalents Consistency Validator ─────────────────────────────────

    [Fact]
    public async Task EquivConsistent_10mmolRef_5mmol_HalfEquiv_Pass()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "Benzaldehyde (1.06 g, 10 mmol) was dissolved in MeOH. NaBH4 (0.19 g, 5 mmol, 0.5 equiv) was added portionwise at 0 °C.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.DoesNotContain(result.Report.Attention,
            a => a.Contains("EQUIV_INCONSISTENT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EquivInconsistent_10mmolRef_5mmol_2equiv_Fails()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "Benzaldehyde (1.06 g, 10 mmol) was dissolved in MeOH. NaBH4 (0.19 g, 5 mmol, 2 equiv) was added portionwise at 0 °C.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.NotNull(result.Report);
        Assert.True(
            result.Report.Attention.Any(a => a.Contains("EQUIV_INCONSISTENT", StringComparison.OrdinalIgnoreCase)),
            "Expected CHEM.EQUIV_INCONSISTENT when stated 2 equiv but actual ratio is 0.5.");
    }

    // ── Evidence Span Integration ────────────────────────────────────────

    [Fact]
    public async Task MalformedChemicalToken_StandaloneC_ProducesEvidenceSnippet()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "The mixture was heated to °C for 2 h, then cooled to room temperature.",
            PolicyProfile = "ScientificTextV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var malformed = result.Artifact.Findings
            .FirstOrDefault(f => f.ValidatorName == "MalformedChemicalTokenValidator"
                              && f.Status == ValidationStatus.Fail);
        Assert.NotNull(malformed);
        Assert.NotNull(malformed.EvidenceStartOffset);
        Assert.NotNull(malformed.EvidenceEndOffset);
        Assert.NotNull(malformed.EvidenceSnippet);
        Assert.Contains("°C", malformed.EvidenceSnippet);

        Assert.NotNull(result.Report);
        Assert.Contains(result.Report.Attention,
            a => a.Contains("\U0001f50e Evidence:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingQuench_EvidenceIncludesSnippet()
    {
        var request = new VerifyTextRequest
        {
            TextToVerify = "NaH (0.24 g, 10 mmol) was added to a solution of the alcohol (1.0 g, 5 mmol) in DMF. The mixture was stirred at 60 °C for 2 h. The product was concentrated under reduced pressure.",
            PolicyProfile = "StrictChemistryV0"
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/verify", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CreateRunResponse? result = await response.Content.ReadFromJsonAsync<CreateRunResponse>(JsonOptions);
        Assert.NotNull(result);

        var quenchFinding = result.Artifact.Findings
            .FirstOrDefault(f => f.ValidatorName == "QuenchWhenReactiveReagentValidator"
                              && f.Status == ValidationStatus.Fail);
        Assert.NotNull(quenchFinding);

        Assert.NotNull(result.Report);
        Assert.True(
            result.Report.Attention.Any(a => a.Contains("MISSING_QUENCH", StringComparison.OrdinalIgnoreCase)),
            "Expected CHEM.MISSING_QUENCH finding for NaH without quench.");
    }
}
