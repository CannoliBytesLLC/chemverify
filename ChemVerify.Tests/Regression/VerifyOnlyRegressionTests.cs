using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests.Regression;

/// <summary>
/// Regression tests derived from 3 VerifyOnly JSON outputs in TestPrompts.txt.
/// Each test validates that segmentation, role classification, and validation
/// produce correct results for real-world chemistry text inputs.
/// </summary>
public class VerifyOnlyRegressionTests
{
    private readonly MalformedChemicalTokenValidator _malformedValidator = new();
    private readonly Guid _runId = Guid.NewGuid();

    private AiRun MakeRun(string text) => new()
    {
        Id = _runId,
        Mode = RunMode.VerifyOnly,
        InputText = text,
        CurrentHash = "regression-test"
    };

    // ── Test 1: NaBH4 narrative with references ──────────────────────────

    private const string Test1Text =
        "The reduction of carbonyl compounds to their corresponding alcohols represents a cornerstone of organic synthesis, with **sodium borohydride ()** remaining the benchmark reagent for this transformation due to its mildness, safety, and high degree of chemoselectivity (Ward & Rhee, 1989; Lamm et al., 2013). While  is inherently capable of reducing both aldehydes and ketones, literature precedents demonstrate that exceptional selectivity for aldehydes over ketones can be achieved by modulating reaction conditions such as temperature and solvent composition (Ward & Rhee, 1989).\n\nTypically, these reductions are conducted in protic media\u2014most commonly **methanol** or **ethanol**\u2014which play a critical role in activating the carbonyl group through hydrogen bonding and assisting in the dissolution of the borohydride salt (Robinson & De Jesus, 1996; Chaudhuri et al., 2010). Kinetic studies have established a clear reactivity hierarchy where **aliphatic aldehydes > aromatic aldehydes > ketones > conjugated enones** (Ward & Rhee, 1989). For instance, performing the reaction at low temperatures (e.g., **\u00b0C**) in a mixed solvent system of alcohol and dichloromethane allows for the nearly exclusive reduction of aldehydes in the presence of more sterically hindered or electronically deactivated ketones (Ward & Rhee, 1989).\n\nBeyond standard homogeneous conditions, recent methodologies have introduced solid-state variants, such as using ** supported on wet silica gel**, which facilitates rapid, solvent-free reductions at room temperature with high yields (Zeynizadeh & Behyar, 2005). These advancements underscore the enduring utility of borohydride-based reagents in modern synthetic programs, providing a reliable pathway to alcohols while tolerating a wide array of other sensitive functional groups including esters, nitriles, and nitro groups (Ward & Rhee, 1989; Lamm et al., 2013).\n\n### References\n\n* Lamm, V., Pan, X., Taniguchi, T., & Curran, D. P. (2013). Reductions of aldehydes and ketones with a readily available N-heterocyclic carbene borane and acetic acid. *Beilstein Journal of Organic Chemistry*, *9*, 675\u2013680. [https://doi.org/10.3762/bjoc.9.76](https://www.google.com/search?q=https://doi.org/10.3762/bjoc.9.76)\n* Robinson, R. K., & De Jesus, K. (1996). The mechanism of NaBH4 addition to aldehydes: A labeling experiment. *Journal of Chemical Education*, *73*(3), 264. [https://doi.org/10.1021/ed073p264](https://www.google.com/search?q=https://doi.org/10.1021/ed073p264)\n* Ward, D. E., & Rhee, C. K. (1989). Chemoselective reductions with sodium borohydride. *Canadian Journal of Chemistry*, *67*(8), 1206\u20131211. [https://doi.org/10.1139/v89-182](https://doi.org/10.1139/v89-182)\n* Zeynizadeh, B., & Behyar, T. (2005). Fast and efficient method for reduction of carbonyl compounds with NaBH4/wet SiO2 under solvent free condition. *Journal of the Brazilian Chemical Society*, *16*(6A), 1200\u20131209. [https://doi.org/10.1590/s0103-50532005000700018](https://www.google.com/search?q=https://doi.org/10.1590/s0103-50532005000700018)\n\n---\n\nWould you like me to find literature precedents for more specialized borohydride reagents, such as **sodium cyanoborohydride** or **L-Selectride**?";

    [Fact]
    public void Test1_ReferencesStartOffset_Detected()
    {
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(Test1Text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(Test1Text, steps);

        Assert.NotNull(ctx.ReferencesStartOffset);
        Assert.True(ctx.ReferencesStartOffset > 0, "ReferencesStartOffset should be positive");
    }

    [Fact]
    public void Test1_ReferenceEntries_NotFragmented()
    {
        // After merging, reference entries should not be split into tiny fragments
        // like "P." or "(2013)." — each reference bullet should be a single step.
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(Test1Text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(Test1Text, steps);
        IReadOnlyList<TextStep> merged = StepMerger.MergeReferenceBlocks(Test1Text, steps, ctx.ReferencesStartOffset);

        // Count steps that fall in the reference section
        int refStepCount = 0;
        foreach (TextStep step in merged)
        {
            if (ctx.ReferencesStartOffset.HasValue && step.StartOffset >= ctx.ReferencesStartOffset.Value)
                refStepCount++;
        }

        // There are 4 reference entries + a "### References" header + a "---" divider + trailing question.
        // After merging, reference section should have at most ~12 steps, not 25+.
        Assert.True(refStepCount <= 12,
            $"Reference section should not be fragmented into many tiny steps. Got {refStepCount}");
    }

    [Fact]
    public void Test1_DoiLines_NotClassifiedAsQuestionOrPrompt()
    {
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(Test1Text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(Test1Text, steps);
        IReadOnlyList<TextStep> merged = StepMerger.MergeReferenceBlocks(Test1Text, steps, ctx.ReferencesStartOffset);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(
            Test1Text, merged, ctx.ReferencesStartOffset);

        // No step containing a DOI URL should be classified as QuestionOrPrompt
        foreach (TextStep step in merged)
        {
            string stepText = Test1Text[step.StartOffset..step.EndOffset];
            if (stepText.Contains("doi.org/"))
            {
                Assert.NotEqual(StepRole.QuestionOrPrompt, roles[step.Index]);
            }
        }
    }

    [Fact]
    public void Test1_TrailingQuestion_ClassifiedAsQuestionOrPrompt()
    {
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(Test1Text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(Test1Text, steps);
        IReadOnlyList<TextStep> merged = StepMerger.MergeReferenceBlocks(Test1Text, steps, ctx.ReferencesStartOffset);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(
            Test1Text, merged, ctx.ReferencesStartOffset);

        // The last step "Would you like me to find..." should be QuestionOrPrompt
        TextStep lastStep = merged[^1];
        string lastText = Test1Text[lastStep.StartOffset..lastStep.EndOffset];
        Assert.Contains("Would you like", lastText);
        Assert.Equal(StepRole.QuestionOrPrompt, roles[lastStep.Index]);
    }

    [Fact]
    public void Test1_MalformedValidator_NoFalsePositivesOnNewlines()
    {
        AiRun run = MakeRun(Test1Text);
        IReadOnlyList<ValidationFinding> findings = _malformedValidator.Validate(_runId, [], run);

        // Real issues: "borohydride ()", standalone "°C", "While  is", "** supported"
        // Should NOT flag \n\n paragraph breaks as "dropped chemical formula"
        int droppedTokenFindings = findings.Count(f =>
            f.Message.Contains("Consecutive spaces") || f.Message.Contains("dropped"));

        // The actual dropped token is "While  is" (double space between words on same line)
        // Paragraph breaks (\n\n) should NOT be counted
        Assert.True(droppedTokenFindings <= 2,
            $"Expected at most 2 dropped-token findings for real inline gaps. Got {droppedTokenFindings}");
    }

    // ── Test 2: Multistep synthesis of Benzocaine ────────────────────────

    private const string Test2Text =
        "### Step 1: Oxidation of *p*-Nitrotoluene to *p*-Nitrobenzoic Acid\n\nThe methyl group of *p*-nitrotoluene is oxidized using a strong oxidant, typically potassium permanganate.\n\n* **Procedure:** To a mixture of *p*-nitrotoluene (1.37 g, 10 mmol) and Na\u2082CO\u2083 (0.5 g) in 40 mL of water, KMnO\u2084 (4.74 g, 30 mmol) is added. The mixture is refluxed for 2 hours until the purple color disappears and a brown MnO\u2082 precipitate forms. After filtration and acidification with c.HCl, the white carboxylic acid precipitate is collected.\n* **Reference:** *Vogel's Textbook of Practical Organic Chemistry*, 5th Ed., Section 6.13.\n* **DOI:** [10.1021/ed056p526](https://www.google.com/search?q=https://doi.org/10.1021/ed056p526) (Note: This DOI discusses variations in oxidation techniques).\n\n---\n\n### Step 2: Fischer Esterification to Ethyl 4-Nitrobenzoate\n\nThe carboxylic acid is converted to an ethyl ester using ethanol and a catalytic acid.\n\n* **Procedure:** *p*-Nitrobenzoic acid (1.67 g, 10 mmol) is dissolved in 20 mL of absolute ethanol. 2 mL of concentrated H\u2082SO\u2084 is added slowly. The solution is refluxed for 1.5 hours. Upon cooling, the mixture is poured into ice water and neutralized with NaHCO\u2083 to precipitate the ester.\n* **Reference:** *Journal of Chemical Education*, 1999, 76, 11, 1551.\n* **DOI:** [10.1021/ed076p1551](https://www.google.com/search?q=https://doi.org/10.1021/ed076p1551)\n\n---\n\n### Step 3: Reduction of the Nitro Group to Benzocaine\n\nThe nitro group is reduced to a primary amine. While  (our previous topic) is generally too weak for nitro reduction without a catalyst, tin(II) chloride or catalytic hydrogenation is typically used.\n\n* **Procedure:** Ethyl 4-nitrobenzoate (1.95 g, 10 mmol) is treated with SnCl\u2082\u00b72H\u2082O (11.2 g, 50 mmol) in 20 mL of ethanol. The mixture is heated to 70\u00b0C for 30 minutes. After cooling, the pH is adjusted to 8-9 with NaOH, and the product is extracted with ethyl acetate.\n* **Yield:** ~80-85% across the final step.\n* **Reference:** *Organic Syntheses*, Coll. Vol. 2, p.299.\n* **DOI:** [10.15227/orgsyn.013.0008](https://doi.org/10.15227/orgsyn.013.0008)\n\n---\n\n### Summary of Transformations\n\n| Step | Reaction Type | Reagents | Key Product |\n| --- | --- | --- | --- |\n| 1 | Benzylic Oxidation | , ,  | *p*-Nitrobenzoic Acid |\n| 2 | Fischer Esterification | , ,  | Ethyl 4-nitrobenzoate |\n| 3 | Nitro Reduction | ,  | **Benzocaine** |\n\nWould you like me to provide the **mechanism** for the Fischer esterification step, or perhaps a different multistep route involving **Grignard reagents**?";

    [Fact]
    public void Test2_FiltrationSentence_ClassifiedAsProcedure()
    {
        // "After filtration and acidification..." contains lab-action nouns;
        // the classifier should recognize these as procedural.
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(Test2Text);
        IReadOnlyList<TextStep> merged = StepMerger.MergeReferenceBlocks(Test2Text, steps, referencesStartOffset: null);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(
            Test2Text, merged, referencesStartOffset: null);

        // Find the step containing "After filtration and acidification"
        bool foundAsProcedure = false;
        foreach (TextStep step in merged)
        {
            string stepText = Test2Text[step.StartOffset..step.EndOffset];
            if (stepText.Contains("filtration") && stepText.Contains("acidification"))
            {
                Assert.Equal(StepRole.Procedure, roles[step.Index]);
                foundAsProcedure = true;
            }
        }

        Assert.True(foundAsProcedure, "Should find a step containing 'filtration and acidification'");
    }

    [Fact]
    public void Test2_SingletonClusters_NotEmitted()
    {
        // 3 distinct time/temp signatures each appearing in 1 step should NOT create clusters.
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(Test2Text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(Test2Text, steps);

        List<ExtractedClaim> claims =
        [
            new()
            {
                Id = Guid.NewGuid(), RunId = _runId,
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "2 h", NormalizedValue = "2", Unit = "h",
                JsonPayload = """{"contextKey":"time","timeAction":"heat"}""",
                StepIndex = 3
            },
            new()
            {
                Id = Guid.NewGuid(), RunId = _runId,
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "1.5 h", NormalizedValue = "1.5", Unit = "h",
                JsonPayload = """{"contextKey":"time","timeAction":"heat"}""",
                StepIndex = 12
            },
            new()
            {
                Id = Guid.NewGuid(), RunId = _runId,
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "70\u00b0C", NormalizedValue = "70", Unit = "\u00b0C",
                JsonPayload = """{"contextKey":"temp"}""",
                StepIndex = 21
            },
            new()
            {
                Id = Guid.NewGuid(), RunId = _runId,
                ClaimType = ClaimType.NumericWithUnit,
                RawText = "30 min", NormalizedValue = "30", Unit = "min",
                JsonPayload = """{"contextKey":"time","timeAction":"heat"}""",
                StepIndex = 21
            }
        ];

        ProcedureSummaryDto result = ProcedureSummaryBuilder.Build(Test2Text, steps, claims, ctx);

        // All clusters are singletons — should be suppressed
        Assert.Empty(result.Clusters);
        Assert.Empty(result.TopIssues);
    }

    [Fact]
    public void Test2_MalformedValidator_NoFalsePositivesOnParagraphBreaks()
    {
        AiRun run = MakeRun(Test2Text);
        IReadOnlyList<ValidationFinding> findings = _malformedValidator.Validate(_runId, [], run);

        // Should NOT flag \n\n between "### Step 1:..." and "The methyl group..." as dropped token
        IReadOnlyList<ValidationFinding> newlineFindings = findings
            .Where(f => f.Message.Contains("dropped") || f.Message.Contains("Consecutive spaces"))
            .Where(f =>
            {
                // Extract position from the message to check what was matched
                string text = run.GetAnalyzedText();
                if (f.EvidenceRef is not null && f.EvidenceRef.StartsWith("AnalyzedText:"))
                {
                    string[] parts = f.EvidenceRef["AnalyzedText:".Length..].Split('-');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                    {
                        string matched = text[start..end];
                        return matched.Contains('\n');
                    }
                }
                return false;
            })
            .ToList();

        Assert.Empty(newlineFindings);
    }

    // ── Test 3: DOI URL with ?q= should not trigger question classification ──

    [Fact]
    public void DoiUrlWithQueryString_NotClassifiedAsQuestion()
    {
        // A reference containing a Google search URL with ?q= should not become QuestionOrPrompt
        string text =
            "The mixture was stirred for 2 h.\n\n### References\n\n" +
            "* Ward, D. E., & Rhee, C. K. (1989). Chemoselective reductions with sodium borohydride. [https://doi.org/10.1139/v89-182](https://www.google.com/search?q=https://doi.org/10.1139/v89-182)";

        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(text);
        ProceduralContext ctx = ProceduralContextDetector.Detect(text, steps);
        IReadOnlyList<TextStep> merged = StepMerger.MergeReferenceBlocks(text, steps, ctx.ReferencesStartOffset);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(
            text, merged, ctx.ReferencesStartOffset);

        foreach (TextStep step in merged)
        {
            string stepText = text[step.StartOffset..step.EndOffset];
            if (stepText.Contains("doi.org/"))
            {
                Assert.NotEqual(StepRole.QuestionOrPrompt, roles[step.Index]);
            }
        }
    }

    [Fact]
    public void Test2_TrailingQuestion_ClassifiedAsQuestionOrPrompt()
    {
        IReadOnlyList<TextStep> steps = StepSegmenter.Segment(Test2Text);
        IReadOnlyList<TextStep> merged = StepMerger.MergeReferenceBlocks(Test2Text, steps, referencesStartOffset: null);
        IReadOnlyDictionary<int, StepRole> roles = StepRoleClassifier.Classify(
            Test2Text, merged, referencesStartOffset: null);

        // The last step "Would you like me to provide the mechanism..." should be QuestionOrPrompt
        TextStep lastStep = merged[^1];
        string lastText = Test2Text[lastStep.StartOffset..lastStep.EndOffset];
        Assert.Contains("Would you like", lastText);
        Assert.Equal(StepRole.QuestionOrPrompt, roles[lastStep.Index]);
    }
}
