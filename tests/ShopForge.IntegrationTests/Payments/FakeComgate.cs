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

    public Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken)
    {
        AskedAbout = true;
        LastMerchant = merchant;

        return Fails
            ? Task.FromException<ComgateTransaction?>(new HttpRequestException("Comgate is unreachable."))
            : Task.FromResult(Says);
    }
}
