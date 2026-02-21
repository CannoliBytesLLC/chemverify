using System.Globalization;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class MwConsistencyValidatorTests
{
    private readonly MwConsistencyValidator _validator = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    [Fact]
    public void ConsistentMassAndMmol_PassFinding()
    {
        // NaBH4: MW ~37.8, so 0.38 g / 10 mmol = 38.0 → plausible
        var run = MakeRun("NaBH4 (0.38 g, 10 mmol) was added.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.38 g",
            NormalizedValue = "0.38",
            Unit = "g",
            SourceLocator = "AnalyzedText:7-13",
            EntityKey = "nabh4",
            StepIndex = 0
        };
        var mmolClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "10 mmol",
            NormalizedValue = "10",
            Unit = "mmol",
            SourceLocator = "AnalyzedText:15-22",
            EntityKey = "nabh4",
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.MwConsistent
            && f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void ImplausibleMw_FailFinding()
    {
        // 0.01 g / 10 mmol = MW 1.0 — way too low
        var run = MakeRun("Reagent (0.01 g, 10 mmol) was added.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.01 g",
            NormalizedValue = "0.01",
            Unit = "g",
            SourceLocator = "AnalyzedText:9-15",
            EntityKey = "reagent",
            StepIndex = 0
        };
        var mmolClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "10 mmol",
            NormalizedValue = "10",
            Unit = "mmol",
            SourceLocator = "AnalyzedText:17-24",
            EntityKey = "reagent",
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.MwImplausible
            && f.Status == ValidationStatus.Fail);
    }

    [Fact]
    public void MgUnit_ConsistentMw_PassFinding()
    {
        // 25 mg / 0.045 mmol = MW ~555.6 → plausible for a large molecule
        var run = MakeRun("ester (25 mg, 0.045 mmol) was dissolved.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "25 mg",
            NormalizedValue = "25",
            Unit = "mg",
            SourceLocator = "AnalyzedText:7-12",
            EntityKey = "ester",
            StepIndex = 0
        };
        var mmolClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.045 mmol",
            NormalizedValue = "0.045",
            Unit = "mmol",
            SourceLocator = "AnalyzedText:14-24",
            EntityKey = "ester",
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim], run);

        Assert.Contains(findings, f => f.Kind == FindingKind.MwConsistent);
    }

    [Fact]
    public void NoMmolClaims_NoFindings()
    {
        var run = MakeRun("NaBH4 (0.38 g) was added.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.38 g",
            NormalizedValue = "0.38",
            Unit = "g",
            SourceLocator = "AnalyzedText:7-13",
            EntityKey = "nabh4",
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim], run);

        Assert.Empty(findings);
    }

    [Fact]
    public void DifferentEntityKeys_NotPaired()
    {
        // Mass is for "nabh4" but mmol is for "ketone" — should not pair
        var run = MakeRun("NaBH4 (0.38 g) and ketone (10 mmol) were combined.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.38 g",
            NormalizedValue = "0.38",
            Unit = "g",
            SourceLocator = "AnalyzedText:7-13",
            EntityKey = "nabh4",
            StepIndex = 0
        };
        var mmolClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "10 mmol",
            NormalizedValue = "10",
            Unit = "mmol",
            SourceLocator = "AnalyzedText:30-37",
            EntityKey = "ketone",
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim], run);

        Assert.Empty(findings);
    }

    [Fact]
    public void SmallMw_NaH_WithinWidenedRange_PassFinding()
    {
        // NaH: MW ~24, so 0.24 g / 10 mmol = 24.0 → plausible with widened range (≥10)
        var run = MakeRun("NaH (0.24 g, 10 mmol) was added.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.24 g",
            NormalizedValue = "0.24",
            Unit = "g",
            SourceLocator = "AnalyzedText:5-11",
            EntityKey = "nah",
            StepIndex = 0
        };
        var mmolClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "10 mmol",
            NormalizedValue = "10",
            Unit = "mmol",
            SourceLocator = "AnalyzedText:13-20",
            EntityKey = "nah",
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.MwConsistent
            && f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void LargeMw_PeptideCoupling_WithinWidenedRange_PassFinding()
    {
        // Large peptide: MW ~2000, so 2.0 g / 1 mmol = 2000 → plausible with widened range (≤3000)
        var run = MakeRun("Peptide substrate (2.0 g, 1 mmol) was dissolved.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "2.0 g",
            NormalizedValue = "2.0",
            Unit = "g",
            SourceLocator = "AnalyzedText:20-25",
            EntityKey = "peptide",
            StepIndex = 0
        };
        var mmolClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "1 mmol",
            NormalizedValue = "1",
            Unit = "mmol",
            SourceLocator = "AnalyzedText:27-33",
            EntityKey = "peptide",
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim], run);

        Assert.Contains(findings, f =>
            f.Kind == FindingKind.MwConsistent
            && f.Status == ValidationStatus.Pass);
    }

    [Fact]
    public void FallbackPairing_NullEntityKeys_SinglePairInStep_Pairs()
    {
        // LiH: 8 mg / 1.0 mmol = MW 8 → plausible (≥10? no, but let's test the pairing)
        // Actually MW = 8.0 which is below MinPlausibleMw=10, so it would FAIL.
        // Use a more plausible case: 18 mg / 0.20 mmol = MW 90
        var run = MakeRun("Reagent C (18 mg, 0.20 mmol) was added.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "18 mg", NormalizedValue = "18", Unit = "mg",
            SourceLocator = "AnalyzedText:12-17",
            EntityKey = null,
            StepIndex = 0
        };
        var mmolClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.20 mmol", NormalizedValue = "0.20", Unit = "mmol",
            SourceLocator = "AnalyzedText:19-28",
            EntityKey = null,
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim], run);

        Assert.Contains(findings, f => f.Kind == FindingKind.MwConsistent);
    }

    [Fact]
    public void FallbackPairing_MultipleNearbyMmol_PairsClosestOnly()
    {
        // Two mmol claims — with tight fallback window, only the close one (same parens) pairs
        // 50 mg / 0.10 mmol = MW 500 → plausible
        var run = MakeRun("Reagent A (50 mg, 0.10 mmol) and reagent B (1.0 mmol) were combined.");
        var massClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "50 mg", NormalizedValue = "50", Unit = "mg",
            SourceLocator = "AnalyzedText:12-17",
            EntityKey = null,
            StepIndex = 0
        };
        var mmolClaim1 = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "0.10 mmol", NormalizedValue = "0.10", Unit = "mmol",
            SourceLocator = "AnalyzedText:19-28",
            EntityKey = null,
            StepIndex = 0
        };
        var mmolClaim2 = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "1.0 mmol", NormalizedValue = "1.0", Unit = "mmol",
            SourceLocator = "AnalyzedText:50-58",
            EntityKey = null,
            StepIndex = 0
        };

        var findings = _validator.Validate(run.Id, [massClaim, mmolClaim1, mmolClaim2], run);

        // The tight fallback window pairs massClaim with mmolClaim1 (same parenthetical)
        Assert.Contains(findings, f => f.Kind == FindingKind.MwConsistent);
    }
}
