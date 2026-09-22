using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Payments;
using Stripe;
using Stripe.Checkout;

namespace ShopForge.Infrastructure.Payments;

internal sealed class StripeNotifications(StripeOptions options, ILogger<StripeNotifications> logger) : IPaymentNotifications
{
    public string Key => StripePaymentProvider.ProviderKey;

    public async Task<PaymentNotification?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        Event stripeEvent;

        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, request.Headers["Stripe-Signature"], options.WebhookSecret);
        }
        catch (StripeException exception)
        {
            logger.LogWarning(exception, "Rejected a Stripe webhook: the signature did not match.");

            return null;
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            return null;
        }

        var result = stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted when session.PaymentStatus == "paid" => PaymentResult.Paid,
            EventTypes.CheckoutSessionExpired or EventTypes.CheckoutSessionAsyncPaymentFailed => PaymentResult.Failed,
            _ => (PaymentResult?)null,
        };

        if (result is null
            || !Guid.TryParse(Metadata(session, StripeMetadata.StoreId), out var storeId)
            || Metadata(session, StripeMetadata.OrderNumber) is not { Length: > 0 } orderNumber)
        {
            return null;
        }

        return new PaymentNotification(stripeEvent.Id, storeId, orderNumber, result.Value);
    }

    private static string? Metadata(Session session, string key) =>
        session.Metadata is not null && session.Metadata.TryGetValue(key, out var value) ? value : null;
}
