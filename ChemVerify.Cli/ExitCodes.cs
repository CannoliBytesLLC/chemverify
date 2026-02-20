namespace ChemVerify.Cli;

/// <summary>
/// Process exit codes returned by the CLI.
/// Designed for CI/CD gate integration.
/// </summary>
public static class ExitCodes
{
    /// <summary>Verdict OK / Low risk - safe to proceed.</summary>
    public const int Ok = 0;

    /// <summary>Medium risk or warnings - review recommended.</summary>
    public const int Warning = 1;

    /// <summary>High or Critical risk - action required.</summary>
    public const int RiskHigh = 2;

    /// <summary>Engine or runtime error - pipeline did not complete.</summary>
    public const int EngineError = 3;
}
