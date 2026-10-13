using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.UnitTests.Payments;

// Checkout already refuses a method whose provider the store has not connected (D-138), so these are the
// adapter's own refusals: the cases that could only arise if a connection were taken apart between a shopper
// being offered the method and paying for it. Creating a payment with half a connection is the thing not to do.
public sealed class ComgatePaymentProviderTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_connection_with_no_credential_behind_it_takes_no_payment()
    {
        var comgate = new RecordingComgate();
        var provider = Provider(comgate, new ProviderConnection("comgate", "MERCHANT", ProviderEnvironment.Test, SecretName: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartAsync(Request, CancellationToken));
        Assert.False(comgate.WasCalled);
    }

    [Fact]
    public async Task A_credential_that_cannot_be_read_takes_no_payment()
    {
        var comgate = new RecordingComgate();
        var provider = Provider(
            comgate,
            new ProviderConnection("comgate", "MERCHANT", ProviderEnvironment.Test, "store-1-comgate"),
            secret: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartAsync(Request, CancellationToken));
        Assert.False(comgate.WasCalled);
    }

    [Fact]
    public async Task A_store_with_no_connection_at_all_takes_no_payment()
    {
        var comgate = new RecordingComgate();
        var provider = Provider(comgate, connection: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartAsync(Request, CancellationToken));
        Assert.False(comgate.WasCalled);
    }

    // Comgate keeps a payment open for a week unless told otherwise. It is told to stop five minutes before the
    // order lets its stock go, within the half hour to a week Comgate accepts (D-193, D-195).
    [Theory]
    [InlineData(35, "30m")]
    [InlineData(125, "120m")]
    [InlineData(20, "30m")]
    [InlineData(60 * 24 * 30, "10080m")]
    public async Task Comgate_stops_taking_the_money_a_little_before_the_stock_goes(int minutesOfStock, string expiry)
    {
        var comgate = new RecordingComgate();
        var provider = Provider(comgate, Connected);

        await provider.StartAsync(Request with { ExpiresAt = Now.AddMinutes(minutesOfStock) }, CancellationToken);

        Assert.Equal(expiry, comgate.LastPayment!.ExpirationTime);
    }

    // The country is whose payment methods Comgate offers, not where the shopper lives: Czech methods for
    // korunas, Slovak ones for euros, as Comgate's own guidance has it.
    [Theory]
    [InlineData("CZK", "CZ")]
    [InlineData("EUR", "SK")]
    public async Task The_methods_offered_are_those_of_the_currency_s_country(string currency, string country)
    {
        var comgate = new RecordingComgate();
        var provider = Provider(comgate, Connected);

        await provider.StartAsync(Request with { Currency = Currency.Of(currency), CountryCode = "IE" }, CancellationToken);

        Assert.Equal((country, "PHYSICAL_GOODS_ONLY"), (comgate.LastPayment!.Country, comgate.LastPayment.Category));
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly ProviderConnection Connected = new("comgate", "MERCHANT", ProviderEnvironment.Test, "store-1-comgate");

    private static PaymentRequest Request => new(
        Guid.CreateVersion7(),
        "2026-00001",
        204.90m,
        Currency.Of("CZK"),
        "buyer@example.test",
        "https://shop.test/order/2026-00001",
        "https://shop.test/cart",
        Now.AddMinutes(35),
        CustomerName: "Alex Buyer");

    private static ComgatePaymentProvider Provider(IComgatePayments comgate, ProviderConnection? connection, string? secret = "s3cret") =>
        new(comgate, new OneConnection(connection), new OneSecret(secret), new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingComgate : IComgatePayments
    {
        public bool WasCalled { get; private set; }

        public ComgatePayment? LastPayment { get; private set; }

        public Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastPayment = payment;

            return Task.FromResult(new ComgateCreated("trans", "https://pay.comgate.test/1"));
        }

        public Task RefundAsync(
            ComgateMerchant merchant, string transactionId, long amountInMinorUnits, bool test, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ComgateTransaction?> FindAsync(ComgateMerchant merchant, string transactionId, CancellationToken cancellationToken) =>
            Task.FromResult<ComgateTransaction?>(null);
    }

    private sealed class OneConnection(ProviderConnection? connection) : IProviderConnections
    {
        public Task<ProviderConnection?> FindAsync(string provider, CancellationToken cancellationToken) => Task.FromResult(connection);

        public Task<IReadOnlyList<string>> ConnectedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(connection is null ? [] : [connection.Provider]);

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
