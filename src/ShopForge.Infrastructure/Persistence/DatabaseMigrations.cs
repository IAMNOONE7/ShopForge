using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ShopForge.Infrastructure.Persistence;

public static class DatabaseMigrations
{
    // Applies what this build carries and the database lacks, and says which those were.
    public static async Task<IReadOnlyList<string>> MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShopForgeDbContext>();
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        await dbContext.Database.MigrateAsync(cancellationToken);

        return pending;
    }
}
