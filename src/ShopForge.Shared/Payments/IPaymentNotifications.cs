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

public enum PaymentResult
{
    Paid,
    Failed,
}
