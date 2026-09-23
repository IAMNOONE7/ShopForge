using ShopForge.Shared.Payments;
using Stripe;

namespace ShopForge.Infrastructure.Payments;

internal interface IRefunds
{
    Task CreateAsync(string paymentReference, long amountInMinorUnits, CancellationToken cancellationToken);
}

internal sealed class StripeRefundApi(StripeOptions options) : IRefunds
{
    private readonly RefundService _refunds = new(new StripeClient(options.SecretKey));

    public Task CreateAsync(string paymentReference, long amountInMinorUnits, CancellationToken cancellationToken) =>
        _refunds.CreateAsync(
            new RefundCreateOptions { PaymentIntent = paymentReference, Amount = amountInMinorUnits },
            cancellationToken: cancellationToken);
}

internal sealed class StripeRefunds(IRefunds refunds) : IPaymentRefunds
{
    public string Key => StripePaymentProvider.ProviderKey;

    public Task RefundAsync(RefundRequest request, CancellationToken cancellationToken) =>
        refunds.CreateAsync(request.PaymentReference, MinorUnits(request.Amount), cancellationToken);

    private static long MinorUnits(decimal amount) => (long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);
}
