using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Core.Configuration;

/// <summary>
/// Represents a single policy profile loaded from configuration.
/// All properties have defaults matching <see cref="Abstractions.Models.PolicySettings"/>,
/// so a JSON profile only needs to specify the fields it overrides.
/// </summary>
public class PolicyProfileDefinition
{
    /// <summary>Output contract the policy requires from the model.</summary>
    public OutputContract RequiredContract { get; set; } = OutputContract.FreeText;

    /// <summary>Whether the pipeline may retry with a reformatting prompt.</summary>
    public bool AllowContractRetry { get; set; } = true;

    /// <summary>Maximum number of reformat-and-retry attempts.</summary>
    public int MaxContractRetries { get; set; } = 1;

    /// <summary>Whether to dampen DOI-fail severity in risk scoring.</summary>
    public bool DampenDoiFailSeverity { get; set; }

    /// <summary>
    /// Validators to enable exclusively. If non-empty, only these validators run.
    /// Mutually exclusive with <see cref="ExcludedValidators"/>.
    /// </summary>
    public List<string> EnabledValidators { get; set; } = [];

    /// <summary>
    /// Validators to exclude. If non-empty, all validators except these run.
    /// Mutually exclusive with <see cref="EnabledValidators"/>.
    /// </summary>
    public List<string> ExcludedValidators { get; set; } = [];

    /// <summary>
    /// Per-validator weight overrides for risk scoring.
    /// Key is the validator name or metadata Id; value is the weight.
    /// </summary>
    public Dictionary<string, double> WeightOverrides { get; set; } = new();
}
