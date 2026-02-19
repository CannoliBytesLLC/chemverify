using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Models;

/// <summary>
/// Resolved settings for a given policy profile name.
/// Controls how the audit pipeline behaves (contract enforcement, retry, validator selection, scoring).
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

    /// <summary>
    /// Validator names to exclude from this policy profile.
    /// If empty, all registered validators run.
    /// </summary>
    public HashSet<string> ExcludedValidators { get; set; } = [];

    /// <summary>
    /// Validator names to include exclusively. If non-empty, only these run.
    /// Takes precedence over <see cref="ExcludedValidators"/>.
    /// </summary>
    public HashSet<string> IncludedValidators { get; set; } = [];

    /// <summary>
    /// Whether to dampen DOI-fail severity in risk scoring (for literature/text profiles).
    /// </summary>
    public bool DampenDoiFailSeverity { get; set; }

    /// <summary>
    /// Per-validator weight overrides for risk scoring.
    /// Key is the validator name or metadata Id; value is the weight.
    /// If empty, default weights from validator metadata apply.
    /// </summary>
    public Dictionary<string, double> WeightOverrides { get; set; } = new();
}
