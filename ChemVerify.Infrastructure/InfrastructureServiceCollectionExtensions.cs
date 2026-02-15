using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Infrastructure.Persistence;
using ChemVerify.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChemVerify.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddChemVerifyInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ChemVerifyDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ChemVerifyRunRepository>();

        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}
