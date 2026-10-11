namespace ShopForge.Shared.Payments;

// Providers that can give the money back themselves. A store whose provider cannot (the manual methods) returns it by
// hand, and the order says so (D-081).
public interface IPaymentRefunds
{
    string Key { get; }

    Task RefundAsync(RefundRequest request, CancellationToken cancellationToken);
}

public sealed record RefundRequest(string OrderNumber, string PaymentReference, decimal Amount, Currency Currency);

// A refund refused before any money could move: the provider was never asked, or the request was one nobody
// could honour. Anything else a refund throws may have reached the provider, so whether the money moved is not
// known, and a caller must not simply try again (D-193).
public sealed class RefundRefusedException(string message) : Exception(message);
