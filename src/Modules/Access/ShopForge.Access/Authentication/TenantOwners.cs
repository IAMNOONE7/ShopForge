using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Access.Users;
using ShopForge.Shared.Access;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Authentication;

// A company taken on by the platform needs somebody who can sign in for it; tenant users are ours, so we invite the
// first one (D-106, D-114).
internal sealed class TenantOwners(DbContext dbContext, InvitationMail mail, TimeProvider clock) : ITenantInitializer
{
    public async Task<string?> FindProblemAsync(NewTenantOwner owner, CancellationToken cancellationToken)
    {
        var email = TenantUser.NormalizeEmail(owner.Email);

        return await dbContext.Set<TenantUser>().IgnoreQueryFilters().AnyAsync(user => user.Email == email, cancellationToken)
            ? "That address already belongs to somebody on ShopForge"
            : null;
    }

    public Task InitializeAsync(Guid tenantId, NewTenantOwner owner, CancellationToken cancellationToken) =>
        mail.SendAsync(tenantId, TenantUser.NormalizeEmail(owner.Email), TenantRole.Owner, owner.InvitedBy, clock.GetUtcNow(), cancellationToken);
}

internal sealed class TenantUserUsage(DbContext dbContext) : ITenantUsage
{
    public async Task<IReadOnlyList<UsageCount>> CountAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken) =>
        [new UsageCount("users", await dbContext.Set<TenantUser>().CountAsync(cancellationToken))];
}
