using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Development;

public static class DevelopmentUsers
{
    public const string OwnerEmail = "owner@demo.local";
    public const string OwnerPassword = "ShopForge-demo-1";

    public static async Task SeedDevelopmentUsersAsync(this IServiceProvider services, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        if (await dbContext.Set<TenantUser>().AnyAsync(cancellationToken))
        {
            return;
        }

        var owner = new TenantUser(tenantId, OwnerEmail, TenantRole.Owner);
        owner.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<TenantUser>>().HashPassword(owner, OwnerPassword));

        dbContext.Add(owner);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
