namespace ChemVerify.API.Contracts;

public class CreateRunRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? ModelName { get; set; } = "mock";
    public string? PolicyProfile { get; set; }
    public string? ConnectorName { get; set; }
    public string? ModelVersion { get; set; }
    public string? ParametersJson { get; set; }
    public string? OutputContract { get; set; }
}

