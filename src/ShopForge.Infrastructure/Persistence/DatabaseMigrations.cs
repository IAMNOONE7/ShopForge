using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ShopForge.Infrastructure.Persistence;

public static class DatabaseMigrations
{
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShopForgeDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
