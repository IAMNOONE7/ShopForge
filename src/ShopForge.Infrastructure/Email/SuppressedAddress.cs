namespace ShopForge.Infrastructure.Email;

// An address the provider told us not to write to again. Kept per store, because a store may only ever see the
// addresses of its own customers (D-102) and because sending reputation is the store's own; like the outbox and
// the record, it belongs to a store loosely, since mail that belongs to no store can bounce too (D-111, D-123).
internal sealed class SuppressedAddress
{
    private SuppressedAddress()
    {
    }

    public SuppressedAddress(Guid? storeId, string email, SuppressionReason reason, string? detail, DateTimeOffset suppressedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Email = email.Trim().ToLowerInvariant();
        Reason = reason;
        Detail = detail is { Length: > 500 } ? detail[..500] : detail;
        SuppressedAt = suppressedAt;
    }

    public Guid Id { get; private set; }

    public Guid? StoreId { get; private set; }

    public string Email { get; private set; } = null!;

    public SuppressionReason Reason { get; private set; }

    public string? Detail { get; private set; }

    public DateTimeOffset SuppressedAt { get; private set; }
}

public enum SuppressionReason
{
    // The address does not exist, or the receiving server refused it for good. Trying again wastes reputation.
    Bounced,

    // Somebody pressed "this is spam". Writing to them again is worse than useless.
    Complained,
}
