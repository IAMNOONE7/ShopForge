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

    public Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken)
    {
        LastMerchant = merchant;
        LastPayment = payment;

        if (Fails)
        {
            return Task.FromException<ComgateCreated>(new HttpRequestException("Comgate is unreachable."));
        }

        Created++;

        return Task.FromResult(new ComgateCreated($"trans-{Created}-{payment.ReferenceId}", $"https://pay.comgate.test/{payment.ReferenceId}/{Created}"));
    }
}
