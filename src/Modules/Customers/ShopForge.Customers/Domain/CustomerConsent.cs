using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

// A yes or a no the customer gave, kept with enough around it to show later what they were answering: the words
// in front of them, when, and from where. One row per purpose per customer, rewritten when they change their
// mind, because what matters is the answer standing now and when it was given (D-119).
internal sealed class CustomerConsent : IStoreOwned
{
    public const int MaxStatementLength = 500;

    private CustomerConsent()
    {
    }

    public CustomerConsent(Guid storeId, Guid storeCustomerId, ConsentPurpose purpose, bool isGranted, string statement, string? ipAddress, DateTimeOffset decidedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        StoreCustomerId = storeCustomerId;
        Purpose = purpose;
        Decide(isGranted, statement, ipAddress, decidedAt);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid StoreCustomerId { get; private set; }

    public ConsentPurpose Purpose { get; private set; }

    public bool IsGranted { get; private set; }

    public string Statement { get; private set; } = null!;

    public string? IpAddress { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public void Decide(bool isGranted, string statement, string? ipAddress, DateTimeOffset decidedAt)
    {
        IsGranted = isGranted;
        Statement = statement.Length > MaxStatementLength ? statement[..MaxStatementLength] : statement;
        IpAddress = ipAddress;
        DecidedAt = decidedAt;
    }
}

// Only one so far, and deliberately so: a purpose exists when something actually sends on it. Transactional mail
// about somebody's own order is not consent-based and is not listed here.
internal enum ConsentPurpose
{
    Marketing,
}
