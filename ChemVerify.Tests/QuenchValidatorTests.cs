using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class QuenchValidatorTests
{
    private readonly QuenchWhenReactiveReagentValidator _validator = new();
    private readonly ReagentRoleExtractor _extractor = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        CreatedUtc = DateTimeOffset.UtcNow,
        Status = RunStatus.Completed,
        Mode = RunMode.VerifyOnly,
        InputText = text,
        Prompt = string.Empty,
        ModelName = "test"
    };

    [Fact]
    public void NarrativeScienceText_MentionsNaBH4_NoMissingQuench_WhenNotProcedural()
    {
        string text =
            "Sodium borohydride (NaBH4) is one of the most commonly used reducing agents in organic chemistry. " +
            "It was first described by Schlesinger and Brown in 1953. " +
            "L-Selectride offers improved stereoselectivity for hindered ketones compared to NaBH4.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingQuench);
    }

    [Fact]
    public void ProcedureText_NaH_NoQuench_StillFlagsMissingQuench()
    {
        string text =
            "To a suspension of NaH (0.48 g, 12 mmol) in dry THF (30 mL) at 0 °C was added " +
            "the alcohol (1.0 g, 10 mmol) dropwise. " +
            "The mixture was stirred at room temperature for 2 h. " +
            "The solvent was removed and the residue was collected.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.Contains(findings, f => f.Kind == FindingKind.MissingQuench);
    }

    [Fact]
    public void MissingQuench_IncludesEvidenceSpanAndSnippet()
    {
        string text =
            "To a solution of the ketone (0.5 g, 5 mmol) in MeOH (10 mL) was added " +
            "NaBH4 (0.19 g, 5 mmol) portionwise at 0 °C. " +
            "The mixture was stirred for 1 h. " +
            "The solvent was removed and the product was collected.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        ValidationFinding? quench = findings.FirstOrDefault(f => f.Kind == FindingKind.MissingQuench);
        Assert.NotNull(quench);

        // Evidence fields must be populated
        Assert.NotNull(quench.EvidenceStartOffset);
        Assert.NotNull(quench.EvidenceEndOffset);
        Assert.NotNull(quench.EvidenceSnippet);
        Assert.Contains("NaBH4", quench.EvidenceSnippet);
    }

    [Fact]
    public void ReferencesSection_IgnoredForQuenchDetection()
    {
        string text =
            "The compound was characterized by NMR.\n" +
            "References\n" +
            "NaBH4 was used extensively by Smith et al.\n";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        // NaBH4 only appears in the references section → should not trigger MissingQuench
        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingQuench);
    }

    [Fact]
    public void ProcedureWithQuench_NoFinding()
    {
        string text =
            "NaBH4 (0.38 g, 10 mmol) was added portionwise at 0 °C. " +
            "After 30 min the reaction was quenched with saturated NH4Cl. " +
            "The mixture was extracted with EtOAc.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingQuench);
    }

    [Fact]
    public void BenignBase_Triethylamine_NoMissingQuench()
    {
        string text =
            "To a solution of the acid chloride (1.0 g, 5 mmol) in DCM (20 mL) was added " +
            "triethylamine (0.76 mL, 5.5 mmol) followed by the amine (0.5 g, 5 mmol). " +
            "The mixture was stirred at room temperature for 12 h. " +
            "The solvent was removed and the product was collected.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingQuench);
    }

    [Fact]
    public void BenignBase_K2CO3_NoMissingQuench()
    {
        string text =
            "A mixture of the aryl halide (1.0 g, 5 mmol), K2CO3 (1.38 g, 10 mmol), " +
            "and Pd(PPh3)4 (0.06 g, 0.05 mmol) in DMF (15 mL) was heated at 80 °C for 6 h. " +
            "The solvent was removed and the residue was purified.";

        AiRun run = MakeRun(text);
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, text);

        IReadOnlyList<ValidationFinding> findings = _validator.Validate(run.Id, claims, run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.MissingQuench);
    }
}
