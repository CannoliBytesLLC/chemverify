using ChemVerify.Core;
using ChemVerify.Infrastructure;

namespace ChemVerify.API.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChemVerifyServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("ChemVerifyDb")
            ?? "Data Source=ChemVerify.db";

        services.AddChemVerifyCore();
        services.AddChemVerifyInfrastructure(connectionString);

        return services;
    }
}