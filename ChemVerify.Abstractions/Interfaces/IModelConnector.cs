namespace ChemVerify.Abstractions.Interfaces;

public interface IModelConnector
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct);
}
