using ShopForge.Infrastructure.Payments.Comgate;

namespace ShopForge.IntegrationTests.Payments;

// Stands in for Comgate. It records what it was asked for, so a test can read what would have gone over the
// wire, and it can refuse, because a gateway that will not answer is a case the checkout already survives.
internal sealed class FakeComgate : IComgatePayments
{
    public ComgateMerchant? LastMerchant { get; private set; }

    public ComgatePayment? LastPayment { get; private set; }

    public int Created { get; private set; }

    public bool Fails { get; set; }

    // What this stand-in will say the transaction is when asked, which is the only answer the reader believes.
    public ComgateTransaction? Says { get; set; }

    public bool AskedAbout { get; private set; }

    // Forget having been asked, so a test can say "and it was not asked again".
    public void Forget() => AskedAbout = false;

    public Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken)
    {
        LastMerchant = merchant;
        LastPayment = payment;

        if (Fails)
        {
            return Task.FromException<ComgateCreated>(new HttpRequestException("Comgate is unreachable."));
        }

        Created++;

        return Task.FromResult(new ComgateCreated($"trans-{Guid.NewGuid():N}", $"https://pay.comgate.test/{payment.ReferenceId}/{Created}"));
    }

    // What was asked to be given back, in the order it was asked, so a test can say "once, and this much".
    public List<(string TransactionId, long Amount, string Currency)> Refunded { get; } = [];

    public Task RefundAsync(
        ComgateMerchant merchant, string transactionId, long amountInMinorUnits, string currency, CancellationToken cancellationToken)
    {
        LastMerchant = merchant;

        if (Fails)
        {
            return Task.FromException(new HttpRequestException("Comgate is unreachable."));
        }

        Refunded.Add((transactionId, amountInMinorUnits, currency));

        return Task.CompletedTask;
    }

    public Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken)
    {
        AskedAbout = true;
        LastMerchant = merchant;

        return Fails
            ? Task.FromException<ComgateTransaction?>(new HttpRequestException("Comgate is unreachable."))
            : Task.FromResult(Says);
    }
}
