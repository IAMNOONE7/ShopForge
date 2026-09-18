using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Persistence;

namespace ShopForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> entityConfigurationAssemblies)
    {
        var connectionString = configuration.GetConnectionString("ShopForge")
            ?? throw new InvalidOperationException("Connection string 'ShopForge' is not configured.");

        services.AddSingleton(new EntityConfigurationAssemblies([.. entityConfigurationAssemblies]));
        services.AddDbContext<ShopForgeDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<ShopForgeDbContext>());

        return services;
    }
}
