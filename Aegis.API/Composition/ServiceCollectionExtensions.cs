using Aegis.Core.Interfaces;
using Aegis.Infrastructure.Connectors;
using Aegis.Infrastructure.Extractors;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Services;
using Aegis.Infrastructure.Validators;
using Microsoft.EntityFrameworkCore;

namespace Aegis.API.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAegisServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EF Core / SQLite
        string connectionString = configuration.GetConnectionString("AegisDb")
            ?? "Data Source=aegis.db";

        services.AddDbContext<AegisDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repository
        services.AddScoped<AegisRunRepository>();

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
