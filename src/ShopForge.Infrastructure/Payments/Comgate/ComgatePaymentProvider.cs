using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Comgate's hosted payment page. The shopper is sent to the address Comgate returns and nowhere else, and the
// amount is the one the committed order came to — nothing here is told anything by a browser (D-057).
internal sealed class ComgatePaymentProvider(
    IComgatePayments payments,
    IProviderConnections connections,
    ISecretStore secrets,
    TimeProvider clock) : IPaymentProvider
{
    public const string ProviderKey = "comgate";

    // Comgate's own word for a basket of goods that are posted, as against vouchers or services.
    private const string PhysicalGoods = "PHYSICAL_GOODS_ONLY";

    // Comgate takes an expiry between half an hour and a week.
    private static readonly TimeSpan ShortestExpiry = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LongestExpiry = TimeSpan.FromDays(7);

    // How long before the stock is released that Comgate stops taking the money, so a payment it accepts in the
    // last moment still reaches an order that is waiting for it.
    private static readonly TimeSpan BeforeTheStockGoes = TimeSpan.FromMinutes(5);

    public string Key => ProviderKey;

    // The merchant is the store's, so a store that has not connected one is not offered this (D-138).
    public bool NeedsConnection => true;

    public async Task<PaymentInstructions> StartAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var merchant = await MerchantAsync(cancellationToken);
        var created = await payments.CreateAsync(
            merchant.Merchant,
            new ComgatePayment(
                request.Currency.ToMinorUnits(request.Amount),
                request.Currency.Code,
                Label(request.OrderNumber),
                request.OrderNumber,
                request.CustomerEmail,
                request.CustomerName,
                string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone,
                MethodCountry(request.Currency),
                request.Language,
                // A parcel locker is a collection point as far as a gateway is concerned; it does not make the
                // shop offer collection from its own counter.
                request.Delivery == PaymentDelivery.ToPickupPoint ? "PICKUP" : "HOME_DELIVERY",
                PhysicalGoods,
                merchant.IsTest,
                Expiry(request.ExpiresAt),
                request.ReturnUrl,
                request.CancelUrl,
                // A payment still in flight comes back to the order, which says it is waiting (D-142).
                request.ReturnUrl),
            cancellationToken);

        return new PaymentInstructions(
            $"Order {request.OrderNumber} is waiting for your payment.",
            created.RedirectUrl,
            created.TransactionId);
    }

    // A connection that has gone, or one whose credential cannot be read, stops the payment here. Creating a
    // live payment because a test flag could not be determined is the failure worth being loud about.
    private async Task<(ComgateMerchant Merchant, bool IsTest)> MerchantAsync(CancellationToken cancellationToken)
    {
        if (await connections.FindAsync(ProviderKey, cancellationToken) is not { } connection || !connection.IsUsable)
        {
            throw new InvalidOperationException("This store has no usable Comgate connection.");
        }

        if (await secrets.FindAsync(connection.SecretName!, cancellationToken) is not { Length: > 0 } secret)
        {
            throw new InvalidOperationException("This store's Comgate credential could not be read.");
        }

        return (new ComgateMerchant(connection.MerchantId, secret), connection.Environment == ProviderEnvironment.Test);
    }

    // Comgate's label is short and is what a shopper sees on their statement.
    private static string Label(string orderNumber) =>
        $"Order {orderNumber}" is { Length: <= 16 } fits ? fits : orderNumber;

    // Which country's payment methods the gateway offers — not where the shopper lives. Comgate's guidance is the
    // Czech methods for korunas and the Slovak ones for euros, and a shopper's own country is refused (D-195).
    private static string MethodCountry(Currency currency) => currency.Code == "EUR" ? "SK" : "CZ";

    // Comgate keeps a payment open for a week unless told otherwise, while the order holds its stock for minutes.
    // It is told to stop a little before the stock goes, so a shopper cannot pay for an order already cancelled —
    // and when that still happens, the payment is kept for a refund rather than lost (D-193).
    private string Expiry(DateTimeOffset stockReleasedAt)
    {
        var open = stockReleasedAt - BeforeTheStockGoes - clock.GetUtcNow();
        var minutes = (int)Math.Clamp(open.TotalMinutes, ShortestExpiry.TotalMinutes, LongestExpiry.TotalMinutes);

        return $"{minutes}m";
    }
}
