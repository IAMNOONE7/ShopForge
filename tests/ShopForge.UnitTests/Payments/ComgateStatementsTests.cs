using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.UnitTests.Payments;

// Reading Comgate's statement lines into ShopForge's terms. What cannot be read is said, never filled in: a line is
// matched on its transaction, its amount and its day, and a guess at any of them is a reconciliation that lies (D-198).
public sealed class ComgateStatementsTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void A_payment_line_is_read_in_crowns_on_the_day_the_shopper_paid()
    {
        var line = ComgateStatements.Read(
            1234567,
            new ComgateTransferLine(1, " AAAA-BBBB-CCCC ", "2026-1", "eur", "10,00", "9,65", "0,35", "2023-01-06 14:21:30", "2023-01-10"));

        Assert.Equal(
            new StatementLine(
                "1234567",
                new DateOnly(2023, 1, 10),
                "AAAA-BBBB-CCCC",
                "2026-1",
                StatementLine.Payment,
                "EUR",
                10.00m,
                0.35m,
                9.65m,
                new DateOnly(2023, 1, 6),
                null),
            line);
    }

    [Theory]
    [InlineData("1 234,50", "1234.50")]
    [InlineData("1\u00A0234,50", "1234.50")]
    [InlineData("-0,15", "-0.15")]
    [InlineData("649", "649")]
    public void Amounts_are_read_with_a_decimal_comma(string written, string expected)
    {
        var line = ComgateStatements.Read(1, Line(confirmed: written));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), line.Confirmed);
        Assert.Null(line.Problem);
    }

    // The reference shows one type, on a payment, and describes no other. A refund or a chargeback is something else
    // nobody has written code for, kept under its own number for a person to look at.
    [Fact]
    public void A_line_of_a_type_nobody_described_is_kept_under_its_own_number()
    {
        var line = ComgateStatements.Read(1, Line() with { Type = 2 });

        Assert.Equal(("type 2", null), (line.Kind, line.Problem));
    }

    // The fee and what was paid out are only added up, so a line without them is still a line.
    [Fact]
    public void A_line_with_no_fee_and_no_payout_is_still_a_line()
    {
        var line = ComgateStatements.Read(1, Line() with { Fee = null, TransferredAmount = null });

        Assert.Equal((null, null, null), (line.Fee, line.Transferred, line.Problem));
    }

    [Theory]
    [InlineData(null, "10,00", "2023-01-06 14:21:30", "no transaction")]
    [InlineData("T-1", "deset", "2023-01-06 14:21:30", "the confirmed amount \"deset\"")]
    [InlineData("T-1", null, "2023-01-06 14:21:30", "no confirmed amount")]
    [InlineData("T-1", "10,00", "06.01.2023", "the payment date \"06.01.2023\"")]
    [InlineData("T-1", "10,00", null, "no payment date")]
    public void A_line_that_cannot_be_read_whole_says_what_could_not_be_read(string? transaction, string? confirmed, string? paidAt, string problem)
    {
        var line = ComgateStatements.Read(1, Line(confirmed: confirmed) with { TransactionId = transaction, PaidAt = paidAt });

        Assert.Equal($"Comgate's line could not be read whole: {problem}.", line.Problem);
    }

    // Comgate settles production payments only, so a test account has no statement to read and is not asked.
    [Fact]
    public async Task A_test_account_is_never_asked_for_its_statements()
    {
        var comgate = new StatementsComgate();
        var statements = new ComgateStatements(comgate, new OneConnection(ProviderEnvironment.Test), new OneSecret("the-secret"));

        Assert.Null(await statements.ReadDayAsync(new DateOnly(2026, 10, 1), CancellationToken));
        Assert.Empty(comgate.DaysAsked);
    }

    [Fact]
    public async Task A_live_account_gives_every_line_of_every_transfer_of_the_day()
    {
        var comgate = new StatementsComgate();
        var statements = new ComgateStatements(comgate, new OneConnection(ProviderEnvironment.Live), new OneSecret("the-secret"));

        var lines = await statements.ReadDayAsync(new DateOnly(2026, 10, 1), CancellationToken);

        Assert.Equal([new DateOnly(2026, 10, 1)], comgate.DaysAsked);
        Assert.Equal(["7:T-1", "7:T-2", "8:T-3"], lines!.Select(line => $"{line.TransferId}:{line.TransactionId}"));
        Assert.Equal(("MERCHANT-1", "the-secret"), (comgate.Merchant!.MerchantId, comgate.Merchant.Secret));
    }

    [Fact]
    public async Task An_account_whose_secret_is_gone_is_not_read()
    {
        var comgate = new StatementsComgate();
        var statements = new ComgateStatements(comgate, new OneConnection(ProviderEnvironment.Live), new OneSecret(null));

        Assert.Null(await statements.ReadDayAsync(new DateOnly(2026, 10, 1), CancellationToken));
        Assert.Empty(comgate.DaysAsked);
    }

    private static ComgateTransferLine Line(string? confirmed = "10,00") =>
        new(1, "T-1", "2026-1", "CZK", confirmed, "9,65", "0,35", "2023-01-06 14:21:30", "2023-01-10");

    private sealed class StatementsComgate : IComgatePayments
    {
        public List<DateOnly> DaysAsked { get; } = [];

        public ComgateMerchant? Merchant { get; private set; }

        public Task<IReadOnlyList<long>> TransferIdsAsync(ComgateMerchant merchant, DateOnly day, CancellationToken cancellationToken)
        {
            Merchant = merchant;
            DaysAsked.Add(day);

            return Task.FromResult<IReadOnlyList<long>>([7, 8]);
        }

        public Task<IReadOnlyList<ComgateTransferLine>> TransferLinesAsync(ComgateMerchant merchant, long transferId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComgateTransferLine>>(transferId == 7
                ? [Line() with { TransactionId = "T-1" }, Line() with { TransactionId = "T-2" }]
                : [Line() with { TransactionId = "T-3" }]);

        public Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RefundAsync(ComgateMerchant merchant, string transactionId, long amountInMinorUnits, bool test, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class OneConnection(ProviderEnvironment environment) : IProviderConnections
    {
        public Task<ProviderConnection?> FindAsync(string provider, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderConnection?>(new ProviderConnection(provider, "MERCHANT-1", environment, "secret-name"));

        public Task<IReadOnlyList<string>> ConnectedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([ComgatePaymentProvider.ProviderKey]);

        public Task<IReadOnlyList<PublishedProviderKey>> PublishedKeysAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublishedProviderKey>>([]);
    }

    private sealed class OneSecret(string? secret) : ISecretStore
    {
        public Task<string?> FindAsync(string name, CancellationToken cancellationToken) => Task.FromResult(secret);

        public Task SetAsync(string name, string value, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ForgetAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
