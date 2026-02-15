using ChemVerify.Core.Enums;

namespace ChemVerify.Core.Models;

/// <summary>
/// Resolved settings for a given policy profile name.
/// Controls how the audit pipeline behaves (contract enforcement, retry, etc.).
/// </summary>
public class PolicySettings
{
    /// <summary>
    /// The output contract the policy requires from the model.
    /// When set to something other than FreeText, overrides the caller-supplied contract.
    /// </summary>
    public OutputContract RequiredContract { get; set; } = OutputContract.FreeText;

    /// <summary>
    /// Whether the pipeline may retry with a reformatting prompt when
    /// structured extraction yields no claims.
    /// </summary>
    public bool AllowContractRetry { get; set; } = true;

    /// <summary>
    /// Maximum number of reformat-and-retry attempts.
    /// </summary>
    public int MaxContractRetries { get; set; } = 1;
}

