using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class YieldMassConsistencyValidatorTests
{
    private readonly YieldMassConsistencyValidator _validator = new();

    private static AiRun MakeRun(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    [Fact]
    public void ConsistentYieldAndMasses_NoFinding()
    {
        // 25 mg starting → 18 mg product, 95% yield
        // Mass recovery = 72%, but stated yield is 95% — this is reasonable
        // because product MW may differ from starting material MW.
        // The validator uses a generous tolerance so this should pass.
        var run = MakeRun("Ester (25 mg, 0.045 mmol) → product (18 mg, 95% yield).");

        var startMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "25 mg", NormalizedValue = "25", Unit = "mg",
            SourceLocator = "AnalyzedText:7-12", StepIndex = 0
        };
        var productMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "18 mg", NormalizedValue = "18", Unit = "mg",
            SourceLocator = "AnalyzedText:40-45", StepIndex = 1
        };
        var yieldClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "95%", NormalizedValue = "95", Unit = "%",
            SourceLocator = "AnalyzedText:47-50", StepIndex = 1,
            JsonPayload = "{\"contextKey\":\"yield\"}"
        };

        var findings = _validator.Validate(run.Id, [startMass, productMass, yieldClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.YieldMassInconsistent);
    }

    [Fact]
    public void ProductMassExceedsStartingMass_WithHighYield_Flags()
    {
        // 10 mg starting → 50 mg product, 80% yield — mass recovery 500%, clearly wrong
        var run = MakeRun("Reagent (10 mg) → product (50 mg, 80% yield).");

        var startMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "10 mg", NormalizedValue = "10", Unit = "mg",
            SourceLocator = "AnalyzedText:9-14", StepIndex = 0
        };
        var productMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "50 mg", NormalizedValue = "50", Unit = "mg",
            SourceLocator = "AnalyzedText:28-33", StepIndex = 1
        };
        var yieldClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "80%", NormalizedValue = "80", Unit = "%",
            SourceLocator = "AnalyzedText:35-38", StepIndex = 1,
            JsonPayload = "{\"contextKey\":\"yield\"}"
        };

        var findings = _validator.Validate(run.Id, [startMass, productMass, yieldClaim], run);

        Assert.Contains(findings, f => f.Kind == FindingKind.YieldMassInconsistent);
    }

    [Fact]
    public void NoYieldClaim_NoFindings()
    {
        var run = MakeRun("Reagent (25 mg) → product (18 mg).");

        var startMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "25 mg", NormalizedValue = "25", Unit = "mg",
            SourceLocator = "AnalyzedText:9-14", StepIndex = 0
        };
        var productMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "18 mg", NormalizedValue = "18", Unit = "mg",
            SourceLocator = "AnalyzedText:28-33", StepIndex = 1
        };

        var findings = _validator.Validate(run.Id, [startMass, productMass], run);

        Assert.Empty(findings);
    }

    [Fact]
    public void HeavierProductWithinAbsoluteBuffer_NoFinding()
    {
        // 50 mg starting → 70 mg product, 85% yield
        // Mass recovery = 140%, but with AbsoluteBufferMg (5 mg) this is within tolerance:
        // toleranceCeiling = 135%, bufferAdjustment = (5/50)*100 = 10%, total = 145%.
        // 140% < 145%, so should NOT fail.
        var run = MakeRun("Starting material A (50 mg) gave product B (70 mg, 85% yield).");

        var startMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "50 mg", NormalizedValue = "50", Unit = "mg",
            SourceLocator = "AnalyzedText:22-27", StepIndex = 0
        };
        var productMass = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "70 mg", NormalizedValue = "70", Unit = "mg",
            SourceLocator = "AnalyzedText:48-53", StepIndex = 1
        };
        var yieldClaim = new ExtractedClaim
        {
            Id = Guid.NewGuid(), RunId = run.Id,
            ClaimType = ClaimType.NumericWithUnit,
            RawText = "85%", NormalizedValue = "85", Unit = "%",
            SourceLocator = "AnalyzedText:55-58", StepIndex = 1,
            JsonPayload = "{\"contextKey\":\"yield\"}"
        };

        var findings = _validator.Validate(run.Id, [startMass, productMass, yieldClaim], run);

        Assert.DoesNotContain(findings, f => f.Kind == FindingKind.YieldMassInconsistent);
    }
}
