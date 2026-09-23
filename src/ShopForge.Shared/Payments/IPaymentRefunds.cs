namespace ShopForge.Shared.Payments;

// Providers that can give the money back themselves. A store whose provider cannot (the manual methods) returns it by
// hand, and the order says so (D-081).
public interface IPaymentRefunds
{
    string Key { get; }

    Task RefundAsync(RefundRequest request, CancellationToken cancellationToken);
}

public sealed record RefundRequest(string OrderNumber, string PaymentReference, decimal Amount, string Currency);
