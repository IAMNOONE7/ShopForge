using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using ShopForge.Shared.Payments;

namespace ShopForge.IntegrationTests.Payments;

// A payment provider a test drives directly: it reports whatever the test posts to its webhook. Stripe's stub
// can only say paid or expired, which is exactly the half of the result space that never needed deciding; this
// one can say a payment is still going, or authorised but not taken (D-140).
internal class FakeGateway : IPaymentProvider, IPaymentNotifications
{
    public const string ProviderKey = "fake-gateway";

    public virtual string Key => ProviderKey;

    public virtual bool NeedsConnection => false;

    public Task<PaymentInstructions> StartAsync(PaymentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentInstructions(
            $"Pay for {request.OrderNumber} at the fake gateway.",
            $"https://gateway.test/{request.OrderNumber}",
            $"gateway-{request.OrderNumber}"));

    public async Task<PaymentNotification?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var posted = await request.ReadFromJsonAsync<PostedNotification>(cancellationToken);

        return posted is null || !Enum.TryParse<PaymentResult>(posted.Result, ignoreCase: true, out var result)
            ? null
            : new PaymentNotification(posted.EventId, posted.StoreId, posted.OrderNumber, result, posted.PaymentReference);
    }

    // What a test posts to /api/payments/fake-gateway.
    public static HttpContent Notification(Guid storeId, string orderNumber, PaymentResult result, string? eventId = null) =>
        JsonContent.Create(new PostedNotification(
            eventId ?? $"event-{Guid.NewGuid():N}",
            storeId,
            orderNumber,
            result.ToString(),
            PaymentReference: $"reference-{orderNumber}"));

    private sealed record PostedNotification(string EventId, Guid StoreId, string OrderNumber, string Result, string? PaymentReference);
}
