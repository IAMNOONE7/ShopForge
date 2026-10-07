using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Comgate tells us to look; it does not tell us what happened. The push names a merchant and a transaction, and
// everything after that is read from Comgate with that merchant's credentials and compared with what we sent
// (D-141). Nothing in the payload is believed on its own — not the status, not the amount, not the reference.
internal sealed class ComgateNotifications(
    ComgateTransactions transactions,
    IPaymentAttempts attempts,
    IMerchantConnections merchants,
    IStoreDirectory stores,
    StoreContext storeContext,
    ILogger<ComgateNotifications> logger) : IPaymentNotifications
{
    public string Key => ComgatePaymentProvider.ProviderKey;

    public async Task<PaymentNotification?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (await Posted(request, cancellationToken) is not { TransactionId: { Length: > 0 } transactionId, Merchant: { Length: > 0 } merchantId })
        {
            logger.LogWarning("A Comgate push arrived without a merchant and a transaction to look up.");

            return null;
        }

        // The merchant narrows this to the stores selling under that account; the transaction tells them apart.
        // A transaction is unique within a merchant, which is the whole reason the pair is the key. `refId` is
        // the order number, unique only per store, so it is never the key — only a thing to agree with.
        var selling = await merchants.StoresAsync(Key, merchantId, cancellationToken);

        if (await attempts.FindAsync(Key, transactionId, selling, cancellationToken) is not { } attempt)
        {
            logger.LogWarning("A Comgate push named transaction {TransactionId}, which no attempt of that merchant started.", transactionId);

            return null;
        }

        if (await stores.FindAsync(attempt.StoreId, cancellationToken) is not { } store)
        {
            logger.LogWarning("A Comgate push named store {StoreId}, which does not exist.", attempt.StoreId);

            return null;
        }

        // From here on everything runs inside the store that attempt belongs to, so the connection read below
        // is that store's and no other. The handler sets the same scope again afterwards, harmlessly.
        storeContext.Set(store.StoreId, store.TenantId);

        // What Comgate says when asked, checked against what was sent. The sweep asks the same question
        // through the same code, which is why a push and a sweep cannot both move the order (D-176).
        return await transactions.ReadAsync(
            new PaymentEnquiry(attempt.StoreId, attempt.OrderNumber, transactionId, attempt.Amount, attempt.Currency),
            cancellationToken);
    }

    private static async Task<PushedPayment?> Posted(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await request.ReadFromJsonAsync<PushedPayment>(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately without the body: a payload that cannot be parsed is still a payload that may carry
            // a credential, and the lesson about logging what arrives was learned once already (18c).
            return null;
        }
    }

    // Only what is needed to know who to ask is read from the push. The rest of what Comgate sends is checked
    // against what it says when asked, so there is nothing to gain by reading it here.
    private sealed record PushedPayment(
        [property: JsonPropertyName("merchant")] string? Merchant,
        [property: JsonPropertyName("transId")] string? TransactionId);
}
