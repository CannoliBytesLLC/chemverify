using ChemVerify.Core.Interfaces;
using ChemVerify.Infrastructure.Connectors;
using ChemVerify.Infrastructure.Extractors;
using ChemVerify.Infrastructure.Persistence;
using ChemVerify.Infrastructure.Services;
using ChemVerify.Infrastructure.Validators;
using Microsoft.EntityFrameworkCore;

namespace ChemVerify.API.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChemVerifyServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EF Core / SQLite
        string connectionString = configuration.GetConnectionString("ChemVerifyDb")
            ?? "Data Source=ChemVerify.db";

        services.AddDbContext<ChemVerifyDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repository
        services.AddScoped<ChemVerifyRunRepository>();

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
        services.AddSingleton<IClaimExtractor>(sp =>
            new CompositeClaimExtractor(new IClaimExtractor[]
            {
                sp.GetRequiredService<DoiClaimExtractor>(),
                sp.GetRequiredService<NumericUnitExtractor>()
            }));

        // Validators
        services.AddSingleton<IValidator, DoiFormatValidator>();
        services.AddSingleton<IValidator, NumericContradictionValidator>();

        // Audit service (orchestrator)
        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}


