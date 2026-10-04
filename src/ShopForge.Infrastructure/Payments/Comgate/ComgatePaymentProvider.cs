using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Comgate's hosted payment page. The shopper is sent to the address Comgate returns and nowhere else, and the
// amount is the one the committed order came to — nothing here is told anything by a browser (D-057).
internal sealed class ComgatePaymentProvider(
    IComgatePayments payments,
    IProviderConnections connections,
    ISecretStore secrets) : IPaymentProvider
{
    public const string ProviderKey = "comgate";

    // Comgate's own word for goods that are posted, as against a ticket or a download.
    private const string PhysicalGoods = "physicalGoods";

    public string Key => ProviderKey;

    // The merchant is the store's, so a store that has not connected one is not offered this (D-138).
    public bool NeedsConnection => true;

    public async Task<PaymentInstructions> StartAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var merchant = await MerchantAsync(cancellationToken);
        var created = await payments.CreateAsync(
            merchant.Merchant,
            new ComgatePayment(
                MinorUnits.Of(request.Amount, request.Currency),
                request.Currency,
                Label(request.OrderNumber),
                request.OrderNumber,
                request.CustomerEmail,
                string.IsNullOrWhiteSpace(request.CustomerName) ? null : request.CustomerName,
                string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone,
                request.CountryCode,
                request.Language,
                // A parcel locker is a collection point as far as a gateway is concerned; it does not make the
                // shop offer collection from its own counter.
                request.Delivery == PaymentDelivery.ToPickupPoint ? "PICKUP" : "HOME_DELIVERY",
                PhysicalGoods,
                merchant.IsTest,
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
}
