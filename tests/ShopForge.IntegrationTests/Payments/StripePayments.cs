using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShopForge.Infrastructure.Payments;
using Stripe;

namespace ShopForge.IntegrationTests.Payments;

internal sealed class FakeCheckoutSessions : ICheckoutSessions
{
    public CheckoutSession? Last { get; private set; }

    public bool Fails { get; set; }

    public Task<string> CreateAsync(CheckoutSession session, CancellationToken cancellationToken)
    {
        Last = session;

        return Fails
            ? Task.FromException<string>(new HttpRequestException("Stripe is unreachable."))
            : Task.FromResult($"https://checkout.stripe.test/{session.OrderNumber}");
    }
}

// Builds the payloads Stripe would post, signed the way Stripe signs them.
internal static class StripeEvents
{
    public static (string Payload, string Signature) Completed(string eventId, Guid storeId, string orderNumber, string secret) =>
        Build(eventId, "checkout.session.completed", storeId, orderNumber, "paid", secret);

    public static (string Payload, string Signature) Expired(string eventId, Guid storeId, string orderNumber, string secret) =>
        Build(eventId, "checkout.session.expired", storeId, orderNumber, "unpaid", secret);

    private static (string Payload, string Signature) Build(
        string eventId,
        string type,
        Guid storeId,
        string orderNumber,
        string paymentStatus,
        string secret)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = eventId,
            type,
            api_version = StripeConfiguration.ApiVersion,
            data = new
            {
                @object = new
                {
                    id = "cs_test_00000000",
                    @object = "checkout.session",
                    payment_status = paymentStatus,
                    metadata = new Dictionary<string, string>
                    {
                        ["shopforge_store_id"] = storeId.ToString(),
                        ["shopforge_order_number"] = orderNumber,
                    },
                },
            },
        });

        return (payload, Sign(payload, secret));
    }

    public static string Sign(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));

        return $"t={timestamp},v1={signature}";
    }
}
