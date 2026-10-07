using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// The refund a return already triggers, going back through Comgate. The returns flow works out what is owed
// and writes it down before any money moves; this is only the call (D-177).
public sealed class ComgateRefundTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public ComgateRefundTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withComgate = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IComgatePayments>(_comgate);
            services.AddSingleton<ISecretStore>(new InMemorySecrets());
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withComgate.Dispose();

    // Everything back, which refunds the delivery with the last of the goods (D-096).
    [Fact]
    public async Task Everything_coming_back_is_refunded_in_full_through_comgate()
    {
        var world = await PaidAtComgateAsync(quantity: 2);

        var received = await ReturnAsync(world, quantity: 2);
        var refund = _comgate.Refunded.Single();

        Assert.Equal(world.TransactionId, refund.TransactionId);
        Assert.Equal("EUR", refund.Currency);
        Assert.Equal(MinorUnits.Of(received.RefundedAmount, "EUR"), refund.Amount);
        Assert.Equal(world.Total, received.RefundedAmount);
    }

    // One of two, which refunds the goods and not the delivery.
    [Fact]
    public async Task Part_of_an_order_coming_back_refunds_only_that_part()
    {
        var world = await PaidAtComgateAsync(quantity: 2);

        var received = await ReturnAsync(world, quantity: 1);
        var refund = _comgate.Refunded.Single();

        Assert.Equal(MinorUnits.Of(received.RefundedAmount, "EUR"), refund.Amount);
        Assert.True(received.RefundedAmount < world.Total, $"a partial refund of {received.RefundedAmount} should be less than {world.Total}");
    }

    // The money-losing mistake. The returns flow claims the row before anything moves, and the adapter must
    // not hand the gateway a second chance at the same money (D-095).
    [Fact]
    public async Task Receiving_the_same_return_twice_refunds_once()
    {
        var world = await PaidAtComgateAsync(quantity: 2);
        var returnId = await RequestedAsync(world, quantity: 2);

        using var accepted = await AdminAsync(world, $"returns/{returnId}/accept");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var first = await AdminAsync(world, $"returns/{returnId}/receive");
        using var again = await AdminAsync(world, $"returns/{returnId}/receive");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(_comgate.Refunded);
    }

    // Cancelling a payment that never completed and refunding one that did are different operations, and the
    // order model keeps them apart. Being asked to refund a transaction Comgate has not marked paid is a
    // mistake, so it is refused here rather than sent to be refused there.
    [Fact]
    public async Task A_transaction_comgate_has_not_marked_paid_is_not_refunded()
    {
        var world = await PaidAtComgateAsync(quantity: 1);
        _comgate.Says = Transaction(world, "CANCELLED");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RefundAsync(world, 10m));

        Assert.Contains("not a payment to refund", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_comgate.Refunded);
    }

    [Fact]
    public async Task A_transaction_comgate_does_not_know_is_not_refunded()
    {
        var world = await PaidAtComgateAsync(quantity: 1);
        _comgate.Says = null;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RefundAsync(world, 10m));

        Assert.Contains("does not know", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_comgate.Refunded);
    }

    // More than the transaction took is a sum from somewhere this adapter cannot see.
    [Fact]
    public async Task More_than_the_transaction_took_is_not_refunded()
    {
        var world = await PaidAtComgateAsync(quantity: 1);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RefundAsync(world, world.Total + 1m));

        Assert.Contains("more than transaction", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_comgate.Refunded);
    }

    // A store that never connected a merchant has nothing to refund through, and saying so is better than
    // sending a call with no credentials on it.
    [Fact]
    public async Task A_store_with_no_usable_connection_cannot_refund_through_comgate()
    {
        var unconnected = await FurnitureStore.CreateAsync(_factory);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RefundAsync(unconnected, "trans-nobody-connected-for", 10m));

        Assert.Contains("no usable Comgate connection", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_comgate.Refunded);
    }

    // A store that settles its own payments is untouched by any of this: its refunds are still by hand.
    [Fact]
    public async Task A_manual_payment_is_not_sent_to_comgate()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        var buyer = await BuyerAsync(furniture);
        var order = await PaidByHandAsync(furniture, buyer);

        var returnId = await RequestedAsync(furniture, buyer, order.Number, "oak-chair", 1);
        using var accepted = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns/{returnId}/accept", null, CancellationToken);
        using var received = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns/{returnId}/receive", null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        Assert.Empty(_comgate.Refunded);
        buyer.Dispose();
    }

    private Task RefundAsync(ComgateWorld world, decimal amount) =>
        RefundAsync(world.Store, world.TransactionId, amount, world.Number);

    private async Task RefundAsync(FurnitureStore store, string transactionId, decimal amount, string orderNumber = "2026-1")
    {
        await using var scope = TestStores.CreateScope(_withComgate.Services, store.Store);
        var refunds = scope.ServiceProvider.GetServices<IPaymentRefunds>()
            .Single(candidate => candidate.Key == ComgatePaymentProvider.ProviderKey);

        await refunds.RefundAsync(new RefundRequest(orderNumber, transactionId, amount, "EUR"), CancellationToken);
    }

    private ComgateTransaction Transaction(ComgateWorld world, string status) =>
        new(world.TransactionId, status, MinorUnits.Of(world.Total, "EUR"), "EUR", world.Number, Test: true);

    private async Task<ReturnView> ReturnAsync(ComgateWorld world, int quantity)
    {
        var returnId = await RequestedAsync(world, quantity);

        using var accepted = await AdminAsync(world, $"returns/{returnId}/accept");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var received = await AdminAsync(world, $"returns/{returnId}/receive");
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);

        return (await received.Content.ReadFromJsonAsync<ReturnView>(CancellationToken))!;
    }

    private Task<Guid> RequestedAsync(ComgateWorld world, int quantity) =>
        RequestedAsync(world.Store, world.Buyer, world.Number, "oak-chair", quantity);

    private async Task<Guid> RequestedAsync(
        FurnitureStore furniture, StorefrontApi buyer, string orderNumber, string product, int quantity)
    {
        using var requested = await buyer.PostAsync(
            $"/api/storefront/account/orders/{orderNumber}/returns",
            new { Lines = new[] { new { StoreProductId = furniture.Products[product], Quantity = quantity } }, Reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        var booked = await furniture.Admin.GetFromJsonAsync<List<ReturnView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns", CancellationToken);

        return booked!.OrderByDescending(candidate => candidate.RequestedAt).First().Id;
    }

    private Task<HttpResponseMessage> AdminAsync(ComgateWorld world, string path) =>
        world.Admin.PostAsync($"/api/admin/stores/{world.Store.Store.StoreId}/{path}", null, CancellationToken);

    private async Task<ComgateWorld> PaidAtComgateAsync(int quantity)
    {
        var merchant = $"MERCHANT-{Guid.NewGuid():N}"[..20];
        var store = await FurnitureStore.CreateAsync(_factory);
        var admin = await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, store.Store.TenantId));

        using var saved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}",
            new { MerchantId = merchant, Environment = "Test", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var kept = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/{ComgatePaymentProvider.ProviderKey}/secret",
            new { Secret },
            CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/payment-methods",
            new { Name = "Comgate", ProviderKey = ComgatePaymentProvider.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        var buyer = await BuyerAsync(store);
        using var added = await buyer.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = quantity });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var attempt = await _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == order.Number, CancellationToken));
        var placedOrder = await _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Number == order.Number, CancellationToken));

        var world = new ComgateWorld(store, admin, buyer, merchant, order.Number, attempt.Reference!, placedOrder.GrandTotal);

        // Comgate confirms it when asked, which is the only way an order becomes paid (D-141).
        _comgate.Says = Transaction(world, "PAID");
        using var client = _withComgate.CreateClient();
        using var pushed = await client.PostAsJsonAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new { merchant, transId = attempt.Reference },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);

        await WaitForInvoiceAsync(store, order.Number);

        return world;
    }

    private async Task<PlacedOrder> PaidByHandAsync(FurnitureStore furniture, StorefrontApi buyer)
    {
        using var added = await buyer.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        await WaitForInvoiceAsync(furniture, order.Number);

        return order;
    }

    // A credit note is issued when a return is received, and that needs the invoice the worker writes.
    private Task WaitForInvoiceAsync(FurnitureStore store, string orderNumber) =>
        _factory.EventuallyAsync(
            () => _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<Invoice>()
                .CountAsync(invoice => invoice.OrderNumber == orderNumber, CancellationToken)),
            count => count > 0,
            CancellationToken);

    private async Task<StorefrontApi> BuyerAsync(FurnitureStore furniture)
    {
        var email = $"refunder-{Guid.NewGuid():N}@example.test";
        var buyer = new StorefrontApi(_withComgate, furniture.Store);

        await buyer.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Robin", LastName = "Sender", Phone = (string?)null });
        var token = await _factory.EventuallyAsync(
            () => Task.FromResult(_factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await buyer.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return buyer;
    }

    private sealed record ComgateWorld(
        FurnitureStore Store,
        HttpClient Admin,
        StorefrontApi Buyer,
        string MerchantId,
        string Number,
        string TransactionId,
        decimal Total);

    private sealed record ReturnView(Guid Id, string Number, string Status, DateTimeOffset RequestedAt, decimal RefundedAmount);
}
