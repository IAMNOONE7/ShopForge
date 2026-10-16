using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Platform.Domain;

namespace ShopForge.Platform.Development;

public static class DevelopmentPlatformUsers
{
    public const string OperatorEmail = "platform@demo.local";
    public const string OperatorPassword = "ShopForge-platform-1";

    public static Task SeedDevelopmentPlatformUsersAsync(this IServiceProvider services, CancellationToken cancellationToken = default) =>
        services.SeedPlatformOperatorAsync(OperatorEmail, OperatorPassword, cancellationToken);

    // The first operator of a database nobody has administered yet. A database that has one already is left alone.
    public static async Task SeedPlatformOperatorAsync(
        this IServiceProvider services, string email, string password, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        if (await dbContext.Set<PlatformUser>().AnyAsync(cancellationToken))
        {
            return;
        }

        var user = new PlatformUser(email);
        user.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<PlatformUser>>().HashPassword(user, password));

        dbContext.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
