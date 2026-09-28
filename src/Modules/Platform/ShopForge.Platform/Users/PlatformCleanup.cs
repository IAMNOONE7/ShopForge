using Microsoft.EntityFrameworkCore;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Platform.Users;

// The same housekeeping for the people who run the platform. Nothing here is tenancy-owned to begin with (D-103).
internal sealed class PlatformCleanup(DbContext dbContext, TimeProvider clock) : IMaintenanceOutsideStores
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public string Name => "Spent operator links";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - Lifetime;

        var resets = await dbContext.Set<PlatformPasswordReset>()
            .Where(reset => reset.ExpiresAt < before)
            .ExecuteDeleteAsync(cancellationToken);

        var invitations = await dbContext.Set<PlatformInvitation>()
            .Where(invitation => invitation.ExpiresAt < before)
            .ExecuteDeleteAsync(cancellationToken);

        return resets + invitations;
    }
}
