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

    // A refund sent and never answered: the request went out, so whether the money moved is not known.
    public bool RefundGoesUnanswered { get; set; }

    // The worse half of that: Comgate gave the money back, and the answer saying so was lost on the way.
    public bool RefundMovesButGoesUnanswered { get; set; }

    // Something that happens while Comgate is still working on a refund, before it answers.
    public Func<Task>? WhileRefunding { get; set; }

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

    // What was asked to be given back, in the order it was asked, so a test can say "once, and this much". No
    // currency: Comgate refunds in the transaction's own, and is told only whether it is a test.
    public List<(string TransactionId, long Amount, bool Test)> Refunded { get; } = [];

    public async Task RefundAsync(
        ComgateMerchant merchant, string transactionId, long amountInMinorUnits, bool test, CancellationToken cancellationToken)
    {
        LastMerchant = merchant;

        if (Fails)
        {
            throw new HttpRequestException("Comgate is unreachable.");
        }

        if (RefundGoesUnanswered)
        {
            throw new TaskCanceledException("Comgate did not answer in time.");
        }

        if (WhileRefunding is { } meanwhile)
        {
            await meanwhile();
        }

        lock (Refunded)
        {
            Refunded.Add((transactionId, amountInMinorUnits, test));
        }

        if (RefundMovesButGoesUnanswered)
        {
            throw new TaskCanceledException("Comgate did not answer in time.");
        }
    }

    public Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken)
    {
        AskedAbout = true;
        LastMerchant = merchant;

        return Fails
            ? Task.FromException<ComgateTransaction?>(new HttpRequestException("Comgate is unreachable."))
            : Task.FromResult(Says);
    }

    // Comgate's statements: the transfers paid out on each day, the lines of each, and every day somebody asked for.
    public Dictionary<DateOnly, List<long>> Transfers { get; } = [];

    public Dictionary<long, List<ComgateTransferLine>> TransferLines { get; } = [];

    public List<DateOnly> StatementDaysAsked { get; } = [];

    public void PaysOut(DateOnly day, long transferId, params ComgateTransferLine[] lines)
    {
        if (!Transfers.TryGetValue(day, out var transfers))
        {
            Transfers[day] = transfers = [];
        }

        transfers.Add(transferId);
        TransferLines[transferId] = [.. lines];
    }

    public Task<IReadOnlyList<long>> TransferIdsAsync(ComgateMerchant merchant, DateOnly day, CancellationToken cancellationToken)
    {
        LastMerchant = merchant;

        lock (StatementDaysAsked)
        {
            StatementDaysAsked.Add(day);
        }

        return Fails
            ? Task.FromException<IReadOnlyList<long>>(new HttpRequestException("Comgate is unreachable."))
            : Task.FromResult<IReadOnlyList<long>>(Transfers.TryGetValue(day, out var transfers) ? [.. transfers] : []);
    }

    public Task<IReadOnlyList<ComgateTransferLine>> TransferLinesAsync(ComgateMerchant merchant, long transferId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ComgateTransferLine>>(TransferLines.TryGetValue(transferId, out var lines) ? [.. lines] : []);
}
