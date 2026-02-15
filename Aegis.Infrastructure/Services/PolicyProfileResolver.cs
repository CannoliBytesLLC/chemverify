using Aegis.Core.Enums;
using Aegis.Core.Models;

namespace Aegis.Infrastructure.Services;

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
        "LenientV0" => new PolicySettings
        {
            RequiredContract = OutputContract.FreeText,
            AllowContractRetry = false,
            MaxContractRetries = 0
        },
        _ => new PolicySettings()
    };
}
