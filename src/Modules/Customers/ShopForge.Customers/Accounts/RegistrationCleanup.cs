using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Customers.Accounts;

// Sign-ups nobody confirmed, and every link that can no longer be followed: a verification, a reset, an address
// change somebody started and left. None of them is worth keeping once it cannot be used.
internal sealed class RegistrationCleanup(DbContext dbContext, TimeProvider clock) : IStoreMaintenance
{
    private static readonly TimeSpan UnconfirmedLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan SpentTokenLifetime = TimeSpan.FromDays(30);

    public string Name => "Unconfirmed sign-ups and spent links";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var registrations = await dbContext.Set<PendingRegistration>()
            .Where(registration => registration.CreatedAt < now - UnconfirmedLifetime)
            .ExecuteDeleteAsync(cancellationToken);

        var tokens = await dbContext.Set<CustomerToken>()
            .Where(token => token.ExpiresAt < now - SpentTokenLifetime)
            .ExecuteDeleteAsync(cancellationToken);

        var changes = await dbContext.Set<EmailChange>()
            .Where(change => change.ExpiresAt < now - SpentTokenLifetime)
            .ExecuteDeleteAsync(cancellationToken);

        return registrations + tokens + changes;
    }
}
