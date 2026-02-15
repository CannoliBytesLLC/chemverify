using ChemVerify.Core.Interfaces;

namespace ChemVerify.Infrastructure.Connectors;

public class MockModelConnector : IModelConnector
{
    public Task<string> GenerateAsync(string prompt, CancellationToken ct)
    {
        string mockOutput =
            "The reaction was carried out at 78 °C for 2 h in 0.5 M aqueous solution, "
            + "achieving a yield of 82%. An alternative route at -78C was also tested with 120 min reaction time. "
            + "See DOI 10.1021/acs.orglett.1c02345 and DOI 10.1038/s41586-020-2649-2 for related procedures.";

        return Task.FromResult(mockOutput);
    }
}

