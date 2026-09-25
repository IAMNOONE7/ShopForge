using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Access;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Authentication;

// A company taken on by the platform needs somebody who can sign in for it; tenant users are ours, so we make the
// first one (D-106).
internal sealed class TenantOwners(DbContext dbContext, IPasswordHasher<TenantUser> passwordHasher) : ITenantInitializer
{
    public async Task InitializeAsync(Guid tenantId, NewTenantOwner owner, CancellationToken cancellationToken)
    {
        var user = new TenantUser(tenantId, owner.Email, TenantRole.Owner);
        user.SetPasswordHash(passwordHasher.HashPassword(user, owner.Password));

        dbContext.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class TenantUserUsage(DbContext dbContext) : ITenantUsage
{
    public async Task<IReadOnlyList<UsageCount>> CountAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken) =>
        [new UsageCount("users", await dbContext.Set<TenantUser>().CountAsync(cancellationToken))];
}
