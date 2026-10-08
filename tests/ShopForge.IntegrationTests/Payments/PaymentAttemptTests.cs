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

// A shopper who abandons a payment and starts again should leave two stories behind, not one overwritten
// (D-141). The order still names the payment that worked; the attempts remember the ones that did not.
public sealed class PaymentAttemptTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly WebApplicationFactory<Program> _withGateway;

    public PaymentAttemptTests(ShopForgeApiFactory factory)
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

    public void Dispose() => _withGateway.Dispose();

    [Fact]
    public async Task Placing_an_order_records_what_was_asked_of_the_provider()
    {
        var world = await GatewayStoreAsync();
        var order = await OrderAsync(world);

        var attempt = Assert.Single(await AttemptsAsync(world, order.Number));

        Assert.Equal(FakeGateway.ProviderKey, attempt.Provider);
        Assert.Equal(PaymentAttemptStatus.Started, attempt.Status);
        // Two chairs at 100 and 4.90 for the courier: what the order came to, not what the browser said.
        Assert.Equal((204.90m, "EUR"), (attempt.Amount, attempt.Currency.Code));
        Assert.Equal($"https://gateway.test/{order.Number}", attempt.RedirectUrl);
        Assert.Equal($"gateway-{order.Number}", attempt.Reference);
    }

    // The thing this slice exists for: a second try does not erase the first.
    [Fact]
    public async Task A_second_order_at_the_same_gateway_is_a_second_attempt()
    {
        var world = await GatewayStoreAsync();
        var first = await OrderAsync(world);
        var second = await OrderAsync(world);

        var attempts = await AttemptsAsync(world);

        Assert.Equal(2, attempts.Count);
        Assert.Equal([first.Number, second.Number], attempts.Select(attempt => attempt.OrderNumber).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task What_the_provider_says_becomes_the_attempt_s_answer()
    {
        var world = await GatewayStoreAsync();
        var waiting = await OrderAsync(world);
        var held = await OrderAsync(world);
        var taken = await OrderAsync(world);
        var refused = await OrderAsync(world);

        using var pending = await NotifyAsync(world, waiting.Number, PaymentResult.Pending);
        using var authorised = await NotifyAsync(world, held.Number, PaymentResult.Authorized);
        using var paid = await NotifyAsync(world, taken.Number, PaymentResult.Paid);
        using var failed = await NotifyAsync(world, refused.Number, PaymentResult.Failed);

        Assert.Equal(PaymentAttemptStatus.Pending, await StatusAsync(world, waiting.Number));
        Assert.Equal(PaymentAttemptStatus.Authorized, await StatusAsync(world, held.Number));
        Assert.Equal(PaymentAttemptStatus.Paid, await StatusAsync(world, taken.Number));
        Assert.Equal(PaymentAttemptStatus.Failed, await StatusAsync(world, refused.Number));
    }

    // A provider that retries a message it already sent, or sends a late one, must not reopen what is settled.
    [Fact]
    public async Task An_attempt_that_has_finished_stays_finished()
    {
        var world = await GatewayStoreAsync();
        var order = await OrderAsync(world);

        using var paid = await NotifyAsync(world, order.Number, PaymentResult.Paid);
        using var late = await NotifyAsync(world, order.Number, PaymentResult.Failed);
        var stored = await OrderRowAsync(world, order.Number);

        Assert.Equal(PaymentAttemptStatus.Paid, await StatusAsync(world, order.Number));
        Assert.Equal(OrderStatus.Paid, stored.Status);
    }

    // A store that takes the money itself has no provider to hear from, so the admin's own confirmation closes
    // the attempt: one answer to "how was this paid for", whatever the method.
    [Fact]
    public async Task Confirming_a_manual_payment_closes_its_attempt_too()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        using var shopper = new StorefrontApi(_factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var started = (await AttemptsAsync(furniture, order.Number)).Single();

        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        var afterwards = (await AttemptsAsync(furniture, order.Number)).Single();

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal((PaymentAttemptStatus.Started, (string?)null), (started.Status, started.Reference));
        Assert.Equal(PaymentAttemptStatus.Paid, afterwards.Status);
    }

    private async Task<HttpResponseMessage> NotifyAsync(FurnitureStore world, string orderNumber, PaymentResult result)
    {
        using var client = _withGateway.CreateClient();

        return await client.PostAsync(
            $"/api/payments/{FakeGateway.ProviderKey}",
            FakeGateway.Notification(world.Store.StoreId, orderNumber, result),
            CancellationToken);
    }

    private async Task<FurnitureStore> GatewayStoreAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        var user = await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId);
        using var admin = await TestUsers.LoginAsync(_withGateway, user);

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-methods",
            new { Name = "Gateway", ProviderKey = FakeGateway.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        return furniture;
    }

    private async Task<PlacedOrder> OrderAsync(FurnitureStore world)
    {
        using var shopper = new StorefrontApi(_withGateway, world.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = world.Products["oak-chair"], Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "gateway"));

        return await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
    }

    private async Task<PaymentAttemptStatus> StatusAsync(FurnitureStore world, string orderNumber) =>
        (await AttemptsAsync(world, orderNumber)).Single().Status;

    private Task<List<PaymentAttempt>> AttemptsAsync(FurnitureStore world, string? orderNumber = null) =>
        _factory.QueryAsync(world.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .Where(attempt => orderNumber == null || attempt.OrderNumber == orderNumber)
            .OrderBy(attempt => attempt.StartedAt)
            .ToListAsync(CancellationToken));

    private Task<Order> OrderRowAsync(FurnitureStore world, string orderNumber) =>
        _factory.QueryAsync(world.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == orderNumber, CancellationToken));
}
