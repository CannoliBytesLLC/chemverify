using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Core.Connectors;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Validators;
using ChemVerify.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ChemVerify.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddChemVerifyCore(this IServiceCollection services)
    {
        // Hash service
        services.AddSingleton<IHashService, HashService>();

        // Canonicalizer
        services.AddSingleton<ICanonicalizer, Canonicalizer>();

        // Risk scorer
        services.AddSingleton<IRiskScorer, RiskScorer>();

        // Model connector (mock for v0)
        services.AddSingleton<IModelConnector, MockModelConnector>();

        // Claim extractors — register individual ones for the composite to consume
        services.AddSingleton<DoiClaimExtractor>();
        services.AddSingleton<NumericUnitExtractor>();
        services.AddSingleton<ReagentRoleExtractor>();
        services.AddSingleton<IClaimExtractor>(sp =>
            new CompositeClaimExtractor(new IClaimExtractor[]
            {
                sp.GetRequiredService<DoiClaimExtractor>(),
                sp.GetRequiredService<NumericUnitExtractor>(),
                sp.GetRequiredService<ReagentRoleExtractor>()
            }));

        // Validators
        services.AddSingleton<IValidator, DoiFormatValidator>();
        services.AddSingleton<IValidator, NumericContradictionValidator>();
        services.AddSingleton<IValidator, IncompatibleReagentSolventValidator>();
        services.AddSingleton<IValidator, MissingSolventValidator>();
        services.AddSingleton<IValidator, MissingTemperatureWhenImpliedValidator>();
        services.AddSingleton<IValidator, MalformedChemicalTokenValidator>();
        services.AddSingleton<IValidator, IncompleteScientificClaimValidator>();
        services.AddSingleton<IValidator, MixedCitationStyleValidator>();
        services.AddSingleton<IValidator, QuenchWhenReactiveReagentValidator>();
        services.AddSingleton<IValidator, DryInertMismatchValidator>();
        services.AddSingleton<IValidator, EquivalentsConsistencyValidator>();

        return services;
    }
}
