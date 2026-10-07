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

    private static PaymentRequest Request => new(
        Guid.CreateVersion7(),
        "2026-00001",
        204.90m,
        "CZK",
        "buyer@example.test",
        "https://shop.test/order/2026-00001",
        "https://shop.test/cart",
        DateTimeOffset.UtcNow.AddMinutes(35));

    private static ComgatePaymentProvider Provider(IComgatePayments comgate, ProviderConnection? connection, string? secret = "s3cret") =>
        new(comgate, new OneConnection(connection), new OneSecret(secret));

    private sealed class RecordingComgate : IComgatePayments
    {
        public bool WasCalled { get; private set; }

        public Task<ComgateCreated> CreateAsync(ComgateMerchant merchant, ComgatePayment payment, CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(new ComgateCreated("trans", "https://pay.comgate.test/1"));
        }

        public Task RefundAsync(
            ComgateMerchant merchant, string transactionId, long amountInMinorUnits, string currency, CancellationToken cancellationToken) =>
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
