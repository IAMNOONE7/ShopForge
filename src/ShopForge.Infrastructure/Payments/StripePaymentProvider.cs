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
                MinorUnits(request.Amount),
                request.Currency,
                request.CustomerEmail,
                request.ReturnUrl,
                request.CancelUrl,
                request.ExpiresAt),
            cancellationToken);

        return new PaymentInstructions($"Order {request.OrderNumber} is waiting for your payment.", started.Url, started.Id);
    }

    // Stripe takes amounts in the currency's smallest unit. Every currency ShopForge supports so far has two decimals;
    // zero-decimal currencies (JPY, HUF) need their own conversion before they can be sold in.
    private static long MinorUnits(decimal amount) => (long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);
}
