using ShopForge.Shared.Connections;

namespace ShopForge.Shared.Payments;

// A provider's callback arrives with no store on it: the request is a machine's, not a shopper's. The stores it
// could possibly concern are settled first, from the merchant account the callback names (IMerchantConnections),
// and only then is the attempt looked for among them (D-141).
public interface IPaymentAttempts
{
    // The stores are the ones the merchant sells under. Passing them rather than searching everywhere is what
    // keeps a reference one gateway reuses from ever reaching another gateway's order.
    Task<RecordedAttempt?> FindAsync(
        string provider,
        string reference,
        IReadOnlyCollection<Guid> stores,
        CancellationToken cancellationToken);
}

public sealed record RecordedAttempt(Guid StoreId, string OrderNumber, decimal Amount, string Currency);
