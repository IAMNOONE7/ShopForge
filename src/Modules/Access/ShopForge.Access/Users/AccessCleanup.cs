using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Users;

// Links that can no longer be followed: a reset somebody used or let expire, an invitation taken up or left. The
// audit trail is where "so-and-so was invited" is kept (D-116), so the row itself is only worth the month it
// takes for somebody to ask what happened.
internal sealed class AccessCleanup(DbContext dbContext, TimeProvider clock) : IMaintenanceOutsideStores
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public string Name => "Spent staff links";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - Lifetime;

        // Nothing is in scope here, so the tenant filter would match nothing at all; these rows belong to every
        // company at once from the worker's point of view.
        var resets = await dbContext.Set<PasswordReset>()
            .IgnoreQueryFilters([TenancyFilters.Tenant])
            .Where(reset => reset.ExpiresAt < before)
            .ExecuteDeleteAsync(cancellationToken);

        var invitations = await dbContext.Set<TenantInvitation>()
            .IgnoreQueryFilters([TenancyFilters.Tenant])
            .Where(invitation => invitation.ExpiresAt < before)
            .ExecuteDeleteAsync(cancellationToken);

        return resets + invitations;
    }
}
