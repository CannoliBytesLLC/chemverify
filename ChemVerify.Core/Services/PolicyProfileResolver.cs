using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Configuration;
using ChemVerify.Core.Validators;
using Microsoft.Extensions.Options;

namespace ChemVerify.Core.Services;

/// <summary>
/// Maps a policy profile name to concrete pipeline settings.
/// External configuration (via <see cref="PolicyProfileOptions"/>) takes precedence;
/// built-in defaults are used as fallback when no config override exists.
/// </summary>
public class PolicyProfileResolver
{
    private readonly IReadOnlyDictionary<string, PolicyProfileDefinition> _configuredProfiles;

    public PolicyProfileResolver(IOptions<PolicyProfileOptions> options)
    {
        _configuredProfiles = options.Value.PolicyProfiles;
    }

    public PolicySettings Resolve(string? profileName)
    {
        if (profileName is null)
            return new PolicySettings();

        // External configuration takes precedence over built-in defaults
        if (_configuredProfiles.TryGetValue(profileName, out PolicyProfileDefinition? definition))
            return MapToSettings(definition);

        // Fall back to built-in profile definitions
        return ResolveBuiltIn(profileName);
    }

    /// <summary>
    /// Built-in profile definitions preserved for backward compatibility.
    /// Used when no external configuration overrides a profile name.
    /// </summary>
    private static PolicySettings ResolveBuiltIn(string profileName) => profileName switch
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
                nameof(IncompatibleReagentSolventValidator),
                nameof(MissingSolventValidator),
                nameof(MissingTemperatureWhenImpliedValidator),
                nameof(QuenchWhenReactiveReagentValidator),
                nameof(DryInertMismatchValidator),
                nameof(EquivalentsConsistencyValidator)
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

    private static PolicySettings MapToSettings(PolicyProfileDefinition definition) => new()
    {
        RequiredContract = definition.RequiredContract,
        AllowContractRetry = definition.AllowContractRetry,
        MaxContractRetries = definition.MaxContractRetries,
        DampenDoiFailSeverity = definition.DampenDoiFailSeverity,
        IncludedValidators = new HashSet<string>(definition.EnabledValidators),
        ExcludedValidators = new HashSet<string>(definition.ExcludedValidators),
        WeightOverrides = new Dictionary<string, double>(definition.WeightOverrides)
    };
}

