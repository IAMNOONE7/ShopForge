using ShopForge.Shared.Payments;

namespace ShopForge.Infrastructure.Payments;

internal sealed class StripePaymentProvider(ICheckoutSessions sessions) : IPaymentProvider
{
    public const string ProviderKey = "stripe";

    public string Key => ProviderKey;

    public async Task<PaymentInstructions> StartAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var started = await sessions.CreateAsync(
            new CheckoutSession(
                request.OrderNumber,
                request.StoreId,
                MinorUnits.Of(request.Amount, request.Currency),
                request.Currency,
                request.CustomerEmail,
                request.ReturnUrl,
                request.CancelUrl,
                request.ExpiresAt),
            cancellationToken);

        return new PaymentInstructions($"Order {request.OrderNumber} is waiting for your payment.", started.Url, started.Id);
    }
}
