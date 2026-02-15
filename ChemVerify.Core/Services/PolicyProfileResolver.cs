using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Validators;

namespace ChemVerify.Core.Services;

/// <summary>
/// Maps a policy profile name to concrete pipeline settings.
/// </summary>
public static class PolicyProfileResolver
{
    public static PolicySettings Resolve(string? profileName) => profileName switch
    {
        "StrictChemistryV0" => new PolicySettings
        {
            RequiredContract = OutputContract.JsonClaimsBlockV1,
            AllowContractRetry = true,
            MaxContractRetries = 1
        },
        "ScientificTextV0" => new PolicySettings
        {
            RequiredContract = OutputContract.FreeText,
            AllowContractRetry = false,
            MaxContractRetries = 0,
            DampenDoiFailSeverity = true,
            ExcludedValidators = new HashSet<string>
            {
                nameof(Validators.IncompatibleReagentSolventValidator),
                nameof(Validators.MissingSolventValidator),
                nameof(Validators.MissingTemperatureWhenImpliedValidator),
                nameof(Validators.QuenchWhenReactiveReagentValidator),
                nameof(Validators.DryInertMismatchValidator),
                nameof(Validators.EquivalentsConsistencyValidator)
            }
        },
        "LenientV0" => new PolicySettings
        {
            RequiredContract = OutputContract.FreeText,
            AllowContractRetry = false,
            MaxContractRetries = 0
        },
        _ => new PolicySettings()
    };
}

