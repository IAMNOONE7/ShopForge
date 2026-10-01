using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;

namespace ShopForge.IntegrationTests.Payments;

// A gateway reports more than "paid" and "failed". Until now anything that was not a payment cancelled the order
// and gave its stock back, which is right for a refusal and wrong for a payment that is simply still going
// (D-140).
public sealed class PaymentResultTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly WebApplicationFactory<Program> _withGateway;

    public PaymentResultTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withGateway = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var gateway = new FakeGateway();
            services.AddSingleton<IPaymentProvider>(gateway);
            services.AddSingleton<IPaymentNotifications>(gateway);
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_payment_that_has_not_finished_leaves_the_order_waiting_and_its_stock_held()
    {
        var world = await OrderAtTheGatewayAsync();

        using var told = await NotifyAsync(world, PaymentResult.Pending);
        var order = await OrderAsync(world);
        var stock = await StockAsync(world);
        var cancellations = await LettersAsync(world, "order.cancelled");

        Assert.Equal(HttpStatusCode.OK, told.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal((world.StockBefore, 2), (stock.OnHand, stock.Reserved));
        Assert.Equal(0, cancellations);
    }

    // Held money is not taken money, and ShopForge does not capture (D-060), so an authorisation moves nothing.
    [Fact]
    public async Task An_authorisation_is_not_a_payment()
    {
        var world = await OrderAtTheGatewayAsync();

        using var told = await NotifyAsync(world, PaymentResult.Authorized);
        var order = await OrderAsync(world);
        var receipts = await LettersAsync(world, "order.paid");

        Assert.Equal(HttpStatusCode.OK, told.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.PaidAt);
        Assert.Equal(0, receipts);
    }

    // The behaviour that was already right, pinned: a refusal is terminal and the items go back on sale.
    [Fact]
    public async Task A_payment_that_will_never_arrive_cancels_the_order_and_frees_the_stock()
    {
        var world = await OrderAtTheGatewayAsync();

        using var told = await NotifyAsync(world, PaymentResult.Failed);
        var order = await OrderAsync(world);
        var stock = await StockAsync(world);

        Assert.Equal(HttpStatusCode.OK, told.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal((world.StockBefore, 0), (stock.OnHand, stock.Reserved));
        Assert.Equal(1, await LettersAsync(world, "order.cancelled"));
    }

    [Fact]
    public async Task A_payment_still_pays()
    {
        var world = await OrderAtTheGatewayAsync();

        using var told = await NotifyAsync(world, PaymentResult.Paid);
        var order = await OrderAsync(world);
        var stock = await StockAsync(world);

        Assert.Equal(HttpStatusCode.OK, told.StatusCode);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal($"reference-{world.Number}", order.PaymentReference);
        Assert.Equal((world.StockBefore - 2, 0), (stock.OnHand, stock.Reserved));
    }

    // The ordinary life of a payment at a gateway that reports as it goes.
    [Fact]
    public async Task A_payment_that_was_pending_can_still_be_paid()
    {
        var world = await OrderAtTheGatewayAsync();

        using var waiting = await NotifyAsync(world, PaymentResult.Pending);
        using var arrived = await NotifyAsync(world, PaymentResult.Paid);
        var order = await OrderAsync(world);

        Assert.Equal(HttpStatusCode.OK, waiting.StatusCode);
        Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    public void Dispose() => _withGateway.Dispose();

    private async Task<HttpResponseMessage> NotifyAsync(GatewayOrder world, PaymentResult result)
    {
        using var client = _withGateway.CreateClient();

        return await client.PostAsync(
            $"/api/payments/{FakeGateway.ProviderKey}",
            FakeGateway.Notification(world.Furniture.Store.StoreId, world.Number, result),
            CancellationToken);
    }

    private async Task<GatewayOrder> OrderAtTheGatewayAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);

        // The gateway only exists on this test's own host, so the admin that adds its method signs in there.
        var user = await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId);
        using var admin = await TestUsers.LoginAsync(_withGateway, user);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-methods",
            new { Name = "Gateway", ProviderKey = FakeGateway.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        using var shopper = new StorefrontApi(_withGateway, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "gateway"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        return new GatewayOrder(furniture, order.Number, FurnitureStore.StockPerProduct);
    }

    private Task<Order> OrderAsync(GatewayOrder world) =>
        _factory.QueryAsync(world.Furniture.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == world.Number, CancellationToken));

    private async Task<StockView> StockAsync(GatewayOrder world)
    {
        var stock = await world.Furniture.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.Single(item => item.VariantId == world.Furniture.VariantIds["oak-chair"]);
    }

    private async Task<int> LettersAsync(GatewayOrder world, string type)
    {
        var payloads = await _factory.QueryAsync(world.Furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {world.Furniture.Store.StoreId} AND type = {type}")
            .ToListAsync(CancellationToken));

        return payloads.Count(payload => payload.Contains(world.Number, StringComparison.Ordinal));
    }

    private sealed record GatewayOrder(FurnitureStore Furniture, string Number, int StockBefore);

    private sealed record StockView(Guid VariantId, int OnHand, int Reserved);
}
