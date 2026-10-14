using ShopForge.Shared.Payments;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// A line of a provider's statement, kept as the provider wrote it. The reconciliation reads these against the
// payments ShopForge recorded; nothing here changes an order (D-198).
internal sealed class ProviderStatementLine : IStoreOwned
{
    public const int MaxProblemLength = 500;

    private ProviderStatementLine()
    {
    }

    public ProviderStatementLine(Guid storeId, string provider, DateOnly day, StatementLine line, DateTimeOffset importedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Provider = provider;
        Day = day;
        TransferId = line.TransferId;
        TransferredOn = line.TransferredOn;
        TransactionId = line.TransactionId;
        OrderNumber = line.OrderNumber;
        Kind = line.Kind;
        Currency = line.Currency;
        Confirmed = line.Confirmed;
        Fee = line.Fee;
        Transferred = line.Transferred;
        PaidOn = line.PaidOn;
        Problem = line.Problem is { Length: > MaxProblemLength } text ? text[..MaxProblemLength] : line.Problem;
        ImportedAt = importedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Provider { get; private set; } = null!;

    // The statement day it was read under: the day the provider paid the transfer out.
    public DateOnly Day { get; private set; }

    public string TransferId { get; private set; } = null!;

    public DateOnly? TransferredOn { get; private set; }

    public string? TransactionId { get; private set; }

    // The provider's copy of the reference ShopForge sent. Shown, never matched on: the transaction is the key.
    public string? OrderNumber { get; private set; }

    public string Kind { get; private set; } = null!;

    public string? Currency { get; private set; }

    public decimal? Confirmed { get; private set; }

    public decimal? Fee { get; private set; }

    public decimal? Transferred { get; private set; }

    // When the shopper paid, in the provider's zone. A line is counted on this day, as ShopForge's payment is.
    public DateOnly? PaidOn { get; private set; }

    public string? Problem { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public bool IsPayment => Kind == StatementLine.Payment && Problem is null;
}

// A statement day that has been read, so it is never asked for again: a day the provider paid nothing out is read
// once and recorded with no lines.
internal sealed class ProviderStatementDay : IStoreOwned
{
    private ProviderStatementDay()
    {
    }

    public ProviderStatementDay(Guid storeId, string provider, DateOnly day, int lines, DateTimeOffset importedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Provider = provider;
        Day = day;
        Lines = lines;
        ImportedAt = importedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Provider { get; private set; } = null!;

    public DateOnly Day { get; private set; }

    public int Lines { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }
}
