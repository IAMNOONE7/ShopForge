using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Email;

// The one question the sending path asks, kept narrow so that path does not need a database to be tested.
internal interface IEmailSuppression
{
    Task<bool> IsSuppressedAsync(string email, CancellationToken cancellationToken);
}

// Who must not be written to, and why. The store filter answers the reading side; the writing side comes from a
// provider's webhook, which names the store the message went out for (D-123).
public sealed class EmailSuppression(DbContext dbContext, IStoreContext storeContext, TimeProvider clock) : IEmailSuppression
{
    public Task<bool> IsSuppressedAsync(string email, CancellationToken cancellationToken)
    {
        var address = Normalize(email);

        return dbContext.Set<SuppressedAddress>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .AnyAsync(
                candidate => candidate.Email == address
                    && (storeContext.StoreId == null ? candidate.StoreId == null : candidate.StoreId == storeContext.StoreId),
                cancellationToken);
    }

    public async Task SuppressAsync(Guid? storeId, string email, SuppressionReason reason, string? detail, CancellationToken cancellationToken)
    {
        var address = Normalize(email);
        var existing = await dbContext.Set<SuppressedAddress>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .SingleOrDefaultAsync(candidate => candidate.Email == address && candidate.StoreId == storeId, cancellationToken);

        if (existing is not null)
        {
            return;
        }

        dbContext.Add(new SuppressedAddress(storeId, address, reason, detail, clock.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SuppressedAddressView>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<SuppressedAddress>()
            .AsNoTracking()
            .OrderByDescending(address => address.SuppressedAt)
            .Take(200)
            .Select(address => new SuppressedAddressView(
                address.Id,
                address.Email,
                address.Reason.ToString(),
                address.Detail,
                address.SuppressedAt))
            .ToListAsync(cancellationToken);

    // A mailbox that was full, or a person who changed their mind: the store can let an address back in.
    public async Task<bool> AllowAgainAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Set<SuppressedAddress>().Where(address => address.Id == id).ExecuteDeleteAsync(cancellationToken) == 1;

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

public sealed record SuppressedAddressView(Guid Id, string Email, string Reason, string? Detail, DateTimeOffset SuppressedAt);
