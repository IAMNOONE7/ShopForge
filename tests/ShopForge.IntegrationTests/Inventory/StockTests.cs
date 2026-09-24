using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Background;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Inventory;

namespace ShopForge.IntegrationTests.Inventory;

public sealed class StockTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stock_is_set_by_the_admin_and_shown_in_the_storefront()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        await furniture.Admin.StockAsync(furniture.ProductIds["oak-chair"], 7);
        var product = await shopper.GetJsonAsync<ProductDetailView>("/api/storefront/products/oak-chair");
        var movements = await MovementsAsync(furniture, "oak-chair");

        Assert.Equal(7, product.Available);
        Assert.Equal([("Adjustment", -43), ("Adjustment", 50)], movements.Select(movement => (movement.Reason, movement.Quantity)).Order());
    }

    [Fact]
    public async Task A_cart_holds_no_more_than_is_in_stock()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await furniture.Admin.StockAsync(furniture.ProductIds["oak-chair"], 3);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["oak-chair"], Quantity = 5 });
        var cart = await shopper.ReadAsync<CartView>(added);

        Assert.Equal(3, cart.Count);
        Assert.Equal(3, cart.Items.Single().Available);
        Assert.True(cart.Changed);
    }

    [Fact]
    public async Task An_order_reserves_stock_and_paying_for_it_takes_the_items_out()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        await furniture.Admin.StockAsync(productId, 5);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 2);

        var order = await PlaceOrderAsync(shopper);
        var reserved = await StockAsync(furniture, productId);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        var afterPayment = await StockAsync(furniture, productId);
        var movements = await MovementsAsync(furniture, "oak-chair");

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal((5, 2, 3), (reserved.OnHand, reserved.Reserved, reserved.Available));
        Assert.Equal((3, 0, 3), (afterPayment.OnHand, afterPayment.Reserved, afterPayment.Available));
        Assert.Contains(movements, movement => movement is { Reason: "Sale", Quantity: -2 } && movement.Reference == order.Number);
    }

    [Fact]
    public async Task Cancelling_an_order_gives_its_stock_back()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        await furniture.Admin.StockAsync(productId, 4);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 3);
        var order = await PlaceOrderAsync(shopper);

        using var cancelled = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/cancel", null, CancellationToken);
        var status = (await cancelled.Content.ReadFromJsonAsync<OrderStatusView>(CancellationToken))!.Status;
        var stock = await StockAsync(furniture, productId);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", status);
        Assert.Equal((4, 0, 4), (stock.OnHand, stock.Reserved, stock.Available));
    }

    [Fact]
    public async Task Checkout_fails_when_the_last_item_went_to_someone_else()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await furniture.Admin.StockAsync(furniture.ProductIds["oak-chair"], 1);
        using var first = new StorefrontApi(factory, furniture.Store);
        using var second = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(first, furniture.Products["oak-chair"], 1);
        await AddToCartAsync(second, furniture.Products["oak-chair"], 1);

        await PlaceOrderAsync(first);
        using var response = await second.PostAsync("/api/storefront/checkout", Checkout.Request());
        var problem = (await response.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Oak Chair", problem.Detail, StringComparison.Ordinal);
    }

    // Two checkouts that reach the last item at the same time: the second reservation waits on the row the first one
    // locked and then finds nothing left, instead of both succeeding.
    [Fact]
    public async Task Concurrent_reservations_for_the_last_item_do_not_oversell()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        await furniture.Admin.StockAsync(productId, 1);
        var request = new[] { new StockRequest(productId, 1) };
        var expiry = DateTimeOffset.UtcNow.AddMinutes(30);

        await using var firstScope = TestStores.CreateScope(factory.Services, furniture.Store);
        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<DbContext>();
        await using var firstTransaction = await firstDbContext.Database.BeginTransactionAsync(CancellationToken);
        var first = await firstScope.ServiceProvider.GetRequiredService<IStockLedger>().ReserveAsync(request, "first", expiry, CancellationToken);

        var second = Task.Run(async () =>
        {
            await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
            var result = await scope.ServiceProvider.GetRequiredService<IStockLedger>().ReserveAsync(request, "second", expiry, CancellationToken);
            await transaction.CommitAsync(CancellationToken);

            return result;
        });

        await firstTransaction.CommitAsync(CancellationToken);
        var secondResult = await second;
        var stock = await StockAsync(furniture, productId);

        Assert.True(first.Succeeded);
        Assert.Equal([productId], secondResult.UnavailableProductIds);
        Assert.Equal((1, 1, 0), (stock.OnHand, stock.Reserved, stock.Available));
    }

    // Two runs releasing the same order at once — the expiry sweep on two instances, or a cancel racing the sweep —
    // both read the reservation as held. Only one of them may give the items back.
    [Fact]
    public async Task Releasing_the_same_order_twice_at_once_gives_the_stock_back_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        await furniture.Admin.StockAsync(productId, 10);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(30);

        // A second order keeps its own items reserved, so a double release shows up as wrong numbers rather than as
        // a check constraint stopping the quantity from going below zero.
        await using var otherScope = TestStores.CreateScope(factory.Services, furniture.Store);
        await otherScope.ServiceProvider.GetRequiredService<IStockLedger>()
            .ReserveAsync([new StockRequest(productId, 4)], "other", expiry, CancellationToken);

        await using var firstScope = TestStores.CreateScope(factory.Services, furniture.Store);
        await firstScope.ServiceProvider.GetRequiredService<IStockLedger>()
            .ReserveAsync([new StockRequest(productId, 3)], "expiring", expiry, CancellationToken);
        var reserved = await StockAsync(furniture, productId);

        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<DbContext>();
        await using var firstTransaction = await firstDbContext.Database.BeginTransactionAsync(CancellationToken);
        await firstScope.ServiceProvider.GetRequiredService<IStockLedger>().ReleaseAsync("expiring", CancellationToken);

        var second = Task.Run(async () =>
        {
            await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
            await scope.ServiceProvider.GetRequiredService<IStockLedger>().ReleaseAsync("expiring", CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }, CancellationToken);

        // The second run has to read the reservation before the first one commits, which is the state two instances
        // of the sweep are in; it then waits on the row the first run has locked.
        await WaitForLockAsync();
        await firstTransaction.CommitAsync(CancellationToken);
        await second;
        var released = await StockAsync(furniture, productId);

        Assert.Equal((10, 7, 3), (reserved.OnHand, reserved.Reserved, reserved.Available));
        Assert.Equal((10, 4, 6), (released.OnHand, released.Reserved, released.Available));
    }

    [Fact]
    public async Task Stock_cannot_be_set_below_what_orders_reserve()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        await furniture.Admin.StockAsync(productId, 5);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 4);
        await PlaceOrderAsync(shopper);

        using var response = await furniture.Admin.SetStockAsync(productId, 2);
        var stock = await StockAsync(furniture, productId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal((5, 4), (stock.OnHand, stock.Reserved));
    }

    [Fact]
    public async Task An_unpaid_order_expires_and_returns_its_stock()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        await furniture.Admin.StockAsync(productId, 6);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 2);
        var order = await PlaceOrderAsync(shopper);

        await ExpireAsync(furniture, order.Number);
        var cancelled = await factory.Services.GetRequiredService<ExpiredOrders>().SweepAsync(CancellationToken);
        var stock = await StockAsync(furniture, productId);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.True(cancelled >= 1);
        Assert.Equal("Cancelled", confirmation.Status);
        Assert.Equal((6, 0, 6), (stock.OnHand, stock.Reserved, stock.Available));
    }

    // Every instance runs the sweep, so two of them regularly meet over the same expired orders. Between them they
    // must cancel each order once: one release of its stock, one letter to the customer.
    [Fact]
    public async Task Two_sweeps_at_once_cancel_each_expired_order_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        await furniture.Admin.StockAsync(productId, 40);
        var numbers = new List<string>();

        for (var order = 0; order < 8; order++)
        {
            using var shopper = new StorefrontApi(factory, furniture.Store);
            await AddToCartAsync(shopper, furniture.Products["oak-chair"], 2);
            numbers.Add((await PlaceOrderAsync(shopper)).Number);
        }

        foreach (var number in numbers)
        {
            await ExpireAsync(furniture, number);
        }

        var sweeps = factory.Services.GetRequiredService<ExpiredOrders>();

        // The sweep walks every store, so its own count includes orders other tests left behind; what this test
        // asserts is what happened to its own.
        await Task.WhenAll(
            Task.Run(() => sweeps.SweepAsync(CancellationToken), CancellationToken),
            Task.Run(() => sweeps.SweepAsync(CancellationToken), CancellationToken));
        var stock = await StockAsync(furniture, productId);
        var letters = await CancellationsAsync(furniture, numbers);
        var cancelled = await factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Set<Order>()
            .CountAsync(order => numbers.Contains(order.Number) && order.Status == OrderStatus.Cancelled, CancellationToken));

        Assert.Equal(numbers.Count, cancelled);
        Assert.Equal((40, 0, 40), (stock.OnHand, stock.Reserved, stock.Available));
        Assert.Equal(numbers.Count, letters);
    }

    [Fact]
    public async Task Stock_of_a_product_from_another_tenant_cannot_be_changed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (otherTenantStore, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var otherAdmin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, otherTenantStore.TenantId));

        using var response = await otherAdmin.SetStockAsync(furniture.ProductIds["oak-chair"], 99);
        var stock = await StockAsync(furniture, furniture.ProductIds["oak-chair"]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(FurnitureStore.StockPerProduct, stock.OnHand);
    }

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrder> PlaceOrderAsync(StorefrontApi shopper)
    {
        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return await shopper.ReadAsync<PlacedOrder>(response, HttpStatusCode.Created);
    }

    private async Task<int> CancellationsAsync(FurnitureStore furniture, IReadOnlyCollection<string> numbers)
    {
        await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var payloads = await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {furniture.Store.StoreId} AND type = 'order.cancelled'")
            .ToListAsync(CancellationToken);

        return payloads.Count(payload => numbers.Any(number => payload.Contains(number, StringComparison.Ordinal)));
    }

    // Waits until another connection is blocked on a row lock, so an interleaving does not depend on timing.
    private async Task WaitForLockAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var waiting = await dbContext.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE NOT granted")
                .SingleAsync(CancellationToken);

            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(50, CancellationToken);
        }
    }

    private async Task<StockView> StockAsync(FurnitureStore furniture, Guid productId)
    {
        var stock = await furniture.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.Single(item => item.ProductId == productId);
    }

    private async Task<List<MovementView>> MovementsAsync(FurnitureStore furniture, string slug)
    {
        var movements = await furniture.Admin.GetFromJsonAsync<List<MovementView>>(
            $"/api/admin/stock/{furniture.ProductIds[slug]}/movements", CancellationToken);

        return movements!;
    }

    private async Task ExpireAsync(FurnitureStore furniture, string number)
    {
        await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Order numbers restart per store, so the store has to be part of the match.
        await dbContext.Database.ExecuteSqlAsync(
            $"UPDATE orders.orders SET reservation_expires_at = now() - interval '1 hour' WHERE number = {number} AND store_id = {furniture.Store.StoreId}",
            CancellationToken);
    }

    private sealed record StockView(Guid ProductId, int OnHand, int Reserved, int Available);

    private sealed record MovementView(DateTimeOffset OccurredAt, int Quantity, string Reason, string Reference);

    private sealed record ProductDetailView(string Slug, string Name, int Available);

    private sealed record OrderStatusView(string Number, string Status);

    private sealed record ProblemView(string Title, string Detail);
}
