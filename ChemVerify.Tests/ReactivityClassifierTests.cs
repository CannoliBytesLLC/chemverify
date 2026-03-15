using ChemVerify.Abstractions.Enums;
using ChemVerify.Core.Validators;

namespace ChemVerify.Tests;

public class ReactivityClassifierTests
{
    [Theory]
    [InlineData("NaH", ReactivityTier.High)]
    [InlineData("n-BuLi", ReactivityTier.High)]
    [InlineData("LiAlH4", ReactivityTier.High)]
    [InlineData("LDA", ReactivityTier.High)]
    [InlineData("LiHMDS", ReactivityTier.High)]
    [InlineData("MeMgBr", ReactivityTier.High)]
    [InlineData("PhMgCl", ReactivityTier.High)]
    [InlineData("DIBAL-H", ReactivityTier.High)]
    [InlineData("9-BBN", ReactivityTier.High)]
    [InlineData("diethylzinc", ReactivityTier.High)]
    public void WhenHighReactivityReagentThenClassifiesHigh(string reagent, ReactivityTier expected)
    {
        Assert.Equal(expected, ReactivityClassifier.Classify(reagent));
    }

    [Theory]
    [InlineData("NaBH4", ReactivityTier.Moderate)]
    [InlineData("sodium borohydride", ReactivityTier.Moderate)]
    [InlineData("NaCNBH3", ReactivityTier.Moderate)]
    [InlineData("SmI2", ReactivityTier.Moderate)]
    [InlineData("SnCl2", ReactivityTier.Moderate)]
    public void WhenModerateReactivityReagentThenClassifiesModerate(string reagent, ReactivityTier expected)
    {
        Assert.Equal(expected, ReactivityClassifier.Classify(reagent));
    }

    [Theory]
    [InlineData("triethylamine", ReactivityTier.Benign)]
    [InlineData("Et3N", ReactivityTier.Benign)]
    [InlineData("TEA", ReactivityTier.Benign)]
    [InlineData("DIPEA", ReactivityTier.Benign)]
    [InlineData("pyridine", ReactivityTier.Benign)]
    [InlineData("DMAP", ReactivityTier.Benign)]
    [InlineData("K2CO3", ReactivityTier.Benign)]
    [InlineData("NaHCO3", ReactivityTier.Benign)]
    [InlineData("NaOH", ReactivityTier.Benign)]
    [InlineData("KOH", ReactivityTier.Benign)]
    [InlineData("DBU", ReactivityTier.Benign)]
    [InlineData("imidazole", ReactivityTier.Benign)]
    [InlineData("sodium carbonate", ReactivityTier.Benign)]
    [InlineData("potassium tert-butoxide", ReactivityTier.Benign)]
    public void WhenBenignReagentThenClassifiesBenign(string reagent, ReactivityTier expected)
    {
        Assert.Equal(expected, ReactivityClassifier.Classify(reagent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("toluene")]
    [InlineData("acetone")]
    public void WhenUnknownOrNullReagentThenClassifiesUnknown(string? reagent)
    {
        Assert.Equal(ReactivityTier.Unknown, ReactivityClassifier.Classify(reagent));
    }

    [Theory]
    [InlineData("NaH", true)]
    [InlineData("NaBH4", true)]
    [InlineData("triethylamine", false)]
    [InlineData("K2CO3", false)]
    [InlineData("toluene", false)]
    public void WhenMayRequireQuenchThenReturnsExpected(string reagent, bool expected)
    {
        Assert.Equal(expected, ReactivityClassifier.MayRequireQuench(reagent));
    }

    [Fact]
    public void WhenNaHCO3ThenNotHigh()
    {
        // NaH negative-lookahead should prevent NaHCO3 from matching High
        Assert.NotEqual(ReactivityTier.High, ReactivityClassifier.Classify("NaHCO3"));
    }
}
