namespace ChemVerify.API.Contracts;

public class VerifyTextRequest
{
    public string TextToVerify { get; set; } = string.Empty;
    public string? PolicyProfile { get; set; }
}
