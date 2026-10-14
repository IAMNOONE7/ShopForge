namespace ShopForge.Shared.Payments;

// What a provider says it paid out, one day at a time: the other side of the books from the payments ShopForge
// recorded (D-198). The store in scope is the one whose account is read.
public interface ISettlementStatements
{
    string Key { get; }

    // The zone the provider counts its statement days in, which is also the zone ShopForge's own payments are
    // counted in when the two are laid side by side.
    TimeZoneInfo Zone { get; }

    // Every line of every transfer the provider paid out on that day. Null when the store has no live account to
    // read: a test payment is never settled, so a test account is never asked.
    Task<IReadOnlyList<StatementLine>?> ReadDayAsync(DateOnly day, CancellationToken cancellationToken);
}

// One line of a provider's statement. A line that cannot be read whole keeps what it could and says why in
// `Problem`, so that it is reported rather than dropped. `Kind` is "payment" for a payment and otherwise the
// provider's own word for the line, which nobody has written code for (D-198).
public sealed record StatementLine(
    string TransferId,
    DateOnly? TransferredOn,
    string? TransactionId,
    string? OrderNumber,
    string Kind,
    string? Currency,
    decimal? Confirmed,
    decimal? Fee,
    decimal? Transferred,
    DateOnly? PaidOn,
    string? Problem)
{
    public const string Payment = "payment";
}
