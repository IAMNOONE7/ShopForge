using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

internal sealed class Discount : IStoreOwned
{
    private Discount()
    {
    }

    public Discount(Guid storeId, string code, string name, DiscountKind kind, decimal value, DiscountLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = Normalize(code);
        Update(name, kind, value, limits, isActive: true);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public DiscountKind Kind { get; private set; }

    // Percent for a percentage, money for an amount, ignored for free shipping.
    public decimal Value { get; private set; }

    public decimal? MinimumOrderAmount { get; private set; }

    public DateTimeOffset? StartsAt { get; private set; }

    public DateTimeOffset? EndsAt { get; private set; }

    public int? MaxRedemptions { get; private set; }

    public int? MaxRedemptionsPerCustomer { get; private set; }

    public int Redemptions { get; private set; }

    public bool IsActive { get; private set; }

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public void Update(string name, DiscountKind kind, decimal value, DiscountLimits limits, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (kind == DiscountKind.Percentage && value is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A percentage is between 0 and 100.");
        }

        if (kind == DiscountKind.Amount && (value <= 0 || decimal.Round(value, 2) != value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An amount is positive, with at most two decimals.");
        }

        Name = name.Trim();
        Kind = kind;
        Value = kind == DiscountKind.FreeShipping ? 0m : value;
        MinimumOrderAmount = limits.MinimumOrderAmount;
        StartsAt = limits.StartsAt;
        EndsAt = limits.EndsAt;
        MaxRedemptions = limits.MaxRedemptions;
        MaxRedemptionsPerCustomer = limits.MaxRedemptionsPerCustomer;
        IsActive = isActive;
    }

    // Everything a code can be refused for except the counted ones, which only checkout can settle (D-086).
    public DiscountProblem? FindProblem(decimal itemsTotal, DateTimeOffset now) => this switch
    {
        { IsActive: false } => DiscountProblem.NotAvailable,
        _ when StartsAt is { } startsAt && now < startsAt => DiscountProblem.NotStarted,
        _ when EndsAt is { } endsAt && now >= endsAt => DiscountProblem.Expired,
        _ when MinimumOrderAmount is { } minimum && itemsTotal < minimum => DiscountProblem.BelowMinimum,
        _ when MaxRedemptions is { } maximum && Redemptions >= maximum => DiscountProblem.UsedUp,
        _ => null,
    };
}

internal sealed record DiscountLimits(
    decimal? MinimumOrderAmount = null,
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? EndsAt = null,
    int? MaxRedemptions = null,
    int? MaxRedemptionsPerCustomer = null);

internal enum DiscountKind
{
    Percentage,
    Amount,
    FreeShipping,
}

internal enum DiscountProblem
{
    Unknown,
    NotAvailable,
    NotStarted,
    Expired,
    BelowMinimum,
    UsedUp,
    AlreadyUsed,
}

// One row per order that used a code: what the per-customer limit is counted against, and the audit trail.
internal sealed class DiscountRedemption : IStoreOwned
{
    private DiscountRedemption()
    {
    }

    public DiscountRedemption(Guid storeId, Guid discountId, string orderNumber, string email, decimal amount, DateTimeOffset redeemedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        DiscountId = discountId;
        OrderNumber = orderNumber;
        Email = email;
        Amount = amount;
        RedeemedAt = redeemedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid DiscountId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public DateTimeOffset RedeemedAt { get; private set; }
}
