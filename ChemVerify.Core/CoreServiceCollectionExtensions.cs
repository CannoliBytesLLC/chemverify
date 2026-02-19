using System.Reflection;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Core.Configuration;
using ChemVerify.Core.Connectors;
using ChemVerify.Core.Extractors;
using ChemVerify.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChemVerify.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers all ChemVerify Core services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Optional configuration root. When provided, policy profiles are loaded
    /// from the <c>ChemVerify:PolicyProfiles</c> section. When <c>null</c>,
    /// built-in profile defaults are used.
    /// </param>
    public static IServiceCollection AddChemVerifyCore(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Policy profile options — bind from config when available,
        // otherwise register empty defaults so IOptions<T> always resolves.
        if (configuration is not null)
        {
            services.Configure<PolicyProfileOptions>(
                configuration.GetSection(PolicyProfileOptions.SectionName));
        }
        else
        {
            services.Configure<PolicyProfileOptions>(_ => { });
        }

        // Startup validation — surfaces OptionsValidationException on first resolution
        services.AddSingleton<IValidateOptions<PolicyProfileOptions>,
            PolicyProfileOptionsValidator>();

        // Policy profile resolver
        services.AddSingleton<PolicyProfileResolver>();

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

        // Validators — automatically discovered via assembly scanning.
        // Finds all concrete, non-abstract classes implementing IValidator
        // in the Core assembly and registers each as a singleton.
        var validatorTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IValidator).IsAssignableFrom(t)
                        && t.IsClass
                        && !t.IsAbstract);

        foreach (var type in validatorTypes)
        {
            services.AddSingleton(typeof(IValidator), type);
        }

        return services;
    }
}
