using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Validators.State;

namespace ChemVerify.Tests;

public class CompoundUnitExtractionTests
{
    private readonly NumericUnitExtractor _extractor = new();

    private static AiRun Run(string text) => new()
    {
        Id = Guid.NewGuid(),
        InputText = text,
        Mode = RunMode.VerifyOnly,
        CurrentHash = "test"
    };

    [Fact]
    public void MolecularWeightWithGPerMol_NotMisparsedAsGrams()
    {
        AiRun run = Run("Compound 1 (MW 1200 g/mol) was used.");
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, run.InputText!);

        Assert.Contains(claims, c => c.Unit == "g/mol" && c.NormalizedValue == "1200");
        Assert.DoesNotContain(claims, c => c.Unit == "g" && c.NormalizedValue == "1200");
    }

    [Fact]
    public void MolarConcentrationWithMolPerL_Recognized()
    {
        AiRun run = Run("A 0.5 mol/L solution was prepared.");
        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, run.InputText!);

        Assert.Contains(claims, c => c.Unit == "mol/L" && c.NormalizedValue == "0.5");
    }

    [Fact]
    public void PhysicallyImplausibleMolecularWeight_Fails()
    {
        AiRun run = Run("The polymer (MW 50000 g/mol) was characterized.");
        PhysicalPlausibilityValidator v = new();

        IReadOnlyList<ExtractedClaim> claims = _extractor.Extract(run.Id, run.InputText!);
        IReadOnlyList<ValidationFinding> findings = v.Validate(run.Id, claims, run);

        Assert.Contains(findings, f => f.Kind == FindingKind.PhysicallyImplausibleValue);
    }
}
