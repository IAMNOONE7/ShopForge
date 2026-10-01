using Stripe;
using Stripe.Checkout;

namespace ShopForge.Infrastructure.Payments;

// The one place that talks to Stripe's API; kept behind an interface so the provider can be exercised without a network.
internal interface ICheckoutSessions
{
    Task<StartedSession> CreateAsync(CheckoutSession session, CancellationToken cancellationToken);
}

// Where the shopper is sent, and what Stripe calls the session they were sent to.
internal sealed record StartedSession(string Id, string Url);

internal sealed record CheckoutSession(
    string OrderNumber,
    Guid StoreId,
    long AmountInMinorUnits,
    string Currency,
    string CustomerEmail,
    string ReturnUrl,
    string CancelUrl,
    DateTimeOffset ExpiresAt);

internal sealed class StripeCheckoutSessions(StripeOptions options) : ICheckoutSessions
{
    private readonly SessionService _sessions = new(new StripeClient(options.SecretKey));

    public async Task<StartedSession> CreateAsync(CheckoutSession session, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            [StripeMetadata.StoreId] = session.StoreId.ToString(),
            [StripeMetadata.OrderNumber] = session.OrderNumber,
        };

        var created = await _sessions.CreateAsync(
            new SessionCreateOptions
            {
                Mode = "payment",
                CustomerEmail = session.CustomerEmail,
                ClientReferenceId = session.OrderNumber,
                SuccessUrl = session.ReturnUrl,
                CancelUrl = session.CancelUrl,
                ExpiresAt = session.ExpiresAt.UtcDateTime,
                Metadata = metadata,
                PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata },
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = session.Currency.ToLowerInvariant(),
                            UnitAmount = session.AmountInMinorUnits,
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = $"Order {session.OrderNumber}" },
                        },
                    },
                ],
            },
            cancellationToken: cancellationToken);

        return new StartedSession(created.Id, created.Url);
    }
}

internal static class StripeMetadata
{
    public const string StoreId = "shopforge_store_id";
    public const string OrderNumber = "shopforge_order_number";
}
