using Microsoft.Extensions.Options;

namespace ChemVerify.Core.Configuration;

/// <summary>
/// Validates <see cref="PolicyProfileOptions"/> when the options are first resolved.
/// Invalid configuration surfaces as an <see cref="OptionsValidationException"/>.
/// </summary>
public class PolicyProfileOptionsValidator : IValidateOptions<PolicyProfileOptions>
{
    public ValidateOptionsResult Validate(string? name, PolicyProfileOptions options)
    {
        List<string> failures = [];

        foreach ((string profileName, PolicyProfileDefinition profile) in options.PolicyProfiles)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                failures.Add("Policy profile name cannot be empty.");
            }

            if (profile.EnabledValidators.Count > 0 && profile.ExcludedValidators.Count > 0)
            {
                failures.Add(
                    $"Profile '{profileName}': EnabledValidators and ExcludedValidators " +
                    "are mutually exclusive — specify one or the other, not both.");
            }

            if (profile.MaxContractRetries < 0)
            {
                failures.Add(
                    $"Profile '{profileName}': MaxContractRetries must be >= 0.");
            }

            foreach ((string validatorId, double weight) in profile.WeightOverrides)
            {
                if (weight < 0.0)
                {
                    failures.Add(
                        $"Profile '{profileName}': WeightOverrides['{validatorId}'] must be >= 0.");
                }
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
