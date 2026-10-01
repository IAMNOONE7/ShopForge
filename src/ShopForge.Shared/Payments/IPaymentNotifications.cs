using Microsoft.AspNetCore.Http;

namespace ShopForge.Shared.Payments;

// Providers that report the result themselves: the request is verified and read by the provider, and what comes back
// is enough to find the order without trusting anything the browser said.
public interface IPaymentNotifications
{
    string Key { get; }

    Task<PaymentNotification?> ReadAsync(HttpRequest request, CancellationToken cancellationToken);
}

public sealed record PaymentNotification(string EventId, Guid StoreId, string OrderNumber, PaymentResult Result, string? PaymentReference = null);

// What a provider says became of a payment. Only two of these are terminal: a payment either happened or it
// never will. The others are a payment still in progress, and an order must not be cancelled for being in one of
// them (D-140) — which is what "anything that is not Paid" used to mean here.
public enum PaymentResult
{
    Paid,

    // Terminal: the money will not arrive. A cancellation by the shopper and a refusal by the bank are both this.
    Failed,

    // The provider has the payment and has not finished with it.
    Pending,

    // The money is held but not taken. It is not a payment until it is captured, and ShopForge does not capture
    // (D-060, D-140), so an order stays where it is.
    Authorized,
}
