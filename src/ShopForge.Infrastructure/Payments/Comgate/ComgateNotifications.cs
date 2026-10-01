using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Comgate tells us to look; it does not tell us what happened. The push names a merchant and a transaction, and
// everything after that is read from Comgate with that merchant's credentials and compared with what we sent
// (D-141). Nothing in the payload is believed on its own — not the status, not the amount, not the reference.
internal sealed class ComgateNotifications(
    IComgatePayments payments,
    IPaymentAttempts attempts,
    IMerchantConnections merchants,
    IProviderConnections connections,
    ISecretStore secrets,
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

        if (await MerchantAsync(cancellationToken) is not { } merchant)
        {
            logger.LogWarning("Store {StoreId} has no usable Comgate connection to check transaction {TransactionId} with.", attempt.StoreId, transactionId);

            return null;
        }

        // A check that cannot be made is not a check that failed: letting this throw gives Comgate a 500 and a
        // retry, where answering "bad request" would tell it to stop asking.
        var transaction = await payments.FindAsync(merchant.Merchant, transactionId, cancellationToken);

        if (transaction is null)
        {
            logger.LogWarning("Comgate does not know transaction {TransactionId}, which a push named.", transactionId);

            return null;
        }

        if (Disagreement(transaction, attempt, merchant.IsTest) is { } disagreement)
        {
            logger.LogWarning(
                "A Comgate push for transaction {TransactionId} disagrees with what was sent: {Disagreement}.",
                transactionId,
                disagreement);

            return null;
        }

        // One event per transaction and state: a repeated message about the same state is recorded once and
        // ignored (D-058), while a payment that goes pending and then pays is two things that both happened.
        return new PaymentNotification(
            $"{transactionId}:{transaction.Status.ToUpperInvariant()}",
            attempt.StoreId,
            attempt.OrderNumber,
            Result(transaction.Status),
            transactionId);
    }

    // Everything the transaction must agree with before an order moves. The first disagreement is the one
    // reported, because a caller who got one of these wrong is not helped by a list.
    private static string? Disagreement(ComgateTransaction transaction, RecordedAttempt attempt, bool expectedTest) =>
        !string.Equals(transaction.ReferenceId, attempt.OrderNumber, StringComparison.Ordinal) ? "a different order"
        : !string.Equals(transaction.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase) ? "a different currency"
        : transaction.PriceInMinorUnits != MinorUnits.Of(attempt.Amount, attempt.Currency) ? "a different amount"
        : transaction.Test != expectedTest ? "the other environment"
        : null;

    private static PaymentResult Result(string status) => status.Trim().ToUpperInvariant() switch
    {
        "PAID" => PaymentResult.Paid,
        "CANCELLED" => PaymentResult.Failed,
        "AUTHORIZED" => PaymentResult.Authorized,
        _ => PaymentResult.Pending,
    };

    private async Task<(ComgateMerchant Merchant, bool IsTest)?> MerchantAsync(CancellationToken cancellationToken)
    {
        if (await connections.FindAsync(Key, cancellationToken) is not { } connection || !connection.IsUsable)
        {
            return null;
        }

        return await secrets.FindAsync(connection.SecretName!, cancellationToken) is { Length: > 0 } secret
            ? (new ComgateMerchant(connection.MerchantId, secret), connection.Environment == ProviderEnvironment.Test)
            : null;
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
