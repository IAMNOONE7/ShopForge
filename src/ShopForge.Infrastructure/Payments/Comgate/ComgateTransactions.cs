using Microsoft.Extensions.Logging;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Asking Comgate what a transaction is now, and turning the answer into something that can move an order.
// Both ways of coming to this question use it: a push tells us to look, and a sweep looks because nobody
// told us. Neither believes anything but what Comgate says when asked (D-141, D-176).
//
// It reads the connection of whichever store is in scope, so the caller sets that first.
internal sealed class ComgateTransactions(
    IComgatePayments payments,
    IProviderConnections connections,
    ISecretStore secrets,
    ILogger<ComgateTransactions> logger)
{
    public async Task<PaymentNotification?> ReadAsync(PaymentEnquiry enquiry, CancellationToken cancellationToken)
    {
        if (await MerchantAsync(cancellationToken) is not { } merchant)
        {
            logger.LogWarning(
                "Store {StoreId} has no usable Comgate connection to check transaction {TransactionId} with.",
                enquiry.StoreId,
                enquiry.Reference);

            return null;
        }

        var transaction = await payments.FindAsync(merchant.Merchant, enquiry.Reference, cancellationToken);

        if (transaction is null)
        {
            logger.LogWarning("Comgate does not know transaction {TransactionId}.", enquiry.Reference);

            return null;
        }

        if (Disagreement(transaction, enquiry, merchant.IsTest) is { } disagreement)
        {
            logger.LogWarning(
                "Comgate's answer for transaction {TransactionId} disagrees with what was sent: {Disagreement}.",
                enquiry.Reference,
                disagreement);

            return null;
        }

        // One event per transaction and state: a repeated answer about the same state is recorded once and
        // ignored (D-058), while a payment that goes pending and then pays is two things that both happened.
        // A sweep and a push that see the same state produce the same id, which is how they cannot both land.
        return new PaymentNotification(
            $"{enquiry.Reference}:{transaction.Status.ToUpperInvariant()}",
            enquiry.StoreId,
            enquiry.OrderNumber,
            Result(transaction.Status),
            enquiry.Reference);
    }

    // Everything the transaction must agree with before an order moves. The first disagreement is the one
    // reported, because a caller who got one of these wrong is not helped by a list.
    private static string? Disagreement(ComgateTransaction transaction, PaymentEnquiry enquiry, bool expectedTest) =>
        !string.Equals(transaction.ReferenceId, enquiry.OrderNumber, StringComparison.Ordinal) ? "a different order"
        : !string.Equals(transaction.Currency, enquiry.Currency.Code, StringComparison.OrdinalIgnoreCase) ? "a different currency"
        : transaction.PriceInMinorUnits != enquiry.Currency.ToMinorUnits(enquiry.Amount) ? "a different amount"
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
        if (await connections.FindAsync(ComgatePaymentProvider.ProviderKey, cancellationToken) is not { } connection || !connection.IsUsable)
        {
            return null;
        }

        return await secrets.FindAsync(connection.SecretName!, cancellationToken) is { Length: > 0 } secret
            ? (new ComgateMerchant(connection.MerchantId, secret), connection.Environment == ProviderEnvironment.Test)
            : null;
    }
}
