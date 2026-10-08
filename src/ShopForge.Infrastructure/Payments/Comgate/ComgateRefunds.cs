using Microsoft.Extensions.Logging;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Payments.Comgate;

// Giving the money back through Comgate. The returns flow works out what is owed and has already written it
// down before this is called, so there is no bookkeeping here — only the call, and the two refusals that would
// otherwise send a request nobody could honour (D-177).
internal sealed class ComgateRefunds(
    IComgatePayments payments,
    IProviderConnections connections,
    ISecretStore secrets,
    ILogger<ComgateRefunds> logger) : IPaymentRefunds
{
    public string Key => ComgatePaymentProvider.ProviderKey;

    public async Task RefundAsync(RefundRequest request, CancellationToken cancellationToken)
    {
        if (await MerchantAsync(cancellationToken) is not { } merchant)
        {
            throw new InvalidOperationException("This store has no usable Comgate connection to refund through.");
        }

        // What Comgate says the transaction is now, which is the only answer believed about it anywhere
        // (D-141). A payment that never completed is cancelled rather than refunded, and the order model keeps
        // those apart — so being asked to refund one is a mistake worth refusing loudly rather than sending to
        // Comgate to be refused there.
        var transaction = await payments.FindAsync(merchant, request.PaymentReference, cancellationToken);

        if (transaction is null)
        {
            throw new InvalidOperationException($"Comgate does not know transaction {request.PaymentReference}.");
        }

        if (!string.Equals(transaction.Status, "PAID", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Comgate transaction {request.PaymentReference} is {transaction.Status}, which is not a payment to refund.");
        }

        var amount = request.Currency.ToMinorUnits(request.Amount);

        // More than the transaction holds is a sum that came from somewhere this adapter cannot see. Comgate
        // would refuse it too; refusing here says which number was wrong.
        if (amount > transaction.PriceInMinorUnits)
        {
            throw new InvalidOperationException(
                $"A refund of {request.Amount} {request.Currency} is more than transaction {request.PaymentReference} took.");
        }

        logger.LogInformation(
            "Refunding {Amount} {Currency} of transaction {TransactionId} for order {OrderNumber}.",
            request.Amount,
            request.Currency,
            request.PaymentReference,
            request.OrderNumber);

        await payments.RefundAsync(merchant, request.PaymentReference, amount, request.Currency.Code, cancellationToken);
    }

    private async Task<ComgateMerchant?> MerchantAsync(CancellationToken cancellationToken)
    {
        if (await connections.FindAsync(Key, cancellationToken) is not { } connection || !connection.IsUsable)
        {
            return null;
        }

        return await secrets.FindAsync(connection.SecretName!, cancellationToken) is { Length: > 0 } secret
            ? new ComgateMerchant(connection.MerchantId, secret)
            : null;
    }
}
