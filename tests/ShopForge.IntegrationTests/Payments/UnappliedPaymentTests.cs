using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Background;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;

namespace ShopForge.IntegrationTests.Payments;

// Money a gateway took that its order could not take. It used to be logged and dropped: the stock had gone back on
// sale, the money was at the gateway, and nobody was told. Now it is kept against the order for the store to give
// back, whatever brought it — a reservation that ran out, a store that cancelled, or a second payment for an order
// that was already paid (D-193).
public sealed class UnappliedPaymentTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly WebApplicationFactory<Program> _withGateway;

    public UnappliedPaymentTests(ShopForgeApiFactory factory)
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
    public async Task A_payment_for_an_order_the_store_cancelled_is_kept_for_a_refund()
    {
        var world = await OrderAtTheGatewayAsync();
        await CancelAsync(world);

        using var paid = await NotifyAsync(world, PaymentResult.Paid);
        var order = await OrderRowAsync(world);
        var kept = (await KeptAsync(world)).Single();
        var stock = await StockAsync(world);

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(
            (UnappliedReason.OrderCancelled, UnappliedPaymentStatus.Open, FakeGateway.ProviderKey, $"reference-{world.Number}"),
            (kept.Reason, kept.Status, kept.Provider, kept.Reference));
        Assert.Equal((order.GrandTotal, order.Currency.Code), (kept.Amount, kept.Currency.Code));

        // The goods stay on sale and the shopper is not told the order is paid: the order has not moved.
        Assert.Equal((world.StockBefore, 0), (stock.OnHand, stock.Reserved));
        Assert.Equal(0, await LettersAsync(world, "order.paid"));
    }

    [Fact]
    public async Task A_payment_arriving_after_the_reservation_ran_out_is_kept()
    {
        var world = await OrderAtTheGatewayAsync();
        await ExpireAsync(world);
        await _withGateway.Services.GetRequiredService<ExpiredOrders>().SweepAsync(CancellationToken);

        using var paid = await NotifyAsync(world, PaymentResult.Paid);
        var kept = (await KeptAsync(world)).Single();

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, (await OrderRowAsync(world)).Status);
        Assert.Equal(UnappliedReason.OrderCancelled, kept.Reason);
    }

    // Two payments for one order, each with its own id at the gateway, so nothing that deduplicates events can
    // tell them apart. The order keeps the first; the second is money the shopper is owed.
    [Fact]
    public async Task A_second_payment_for_a_paid_order_is_kept()
    {
        var world = await OrderAtTheGatewayAsync();

        using var first = await NotifyAsync(world, PaymentResult.Paid);
        using var second = await NotifyAsync(world, PaymentResult.Paid, reference: $"second-{world.Number}");
        var order = await OrderRowAsync(world);
        var kept = (await KeptAsync(world)).Single();

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal((OrderStatus.Paid, $"reference-{world.Number}"), (order.Status, order.PaymentReference));
        Assert.Equal((UnappliedReason.OrderAlreadyPaid, $"second-{world.Number}"), (kept.Reason, kept.Reference));
        Assert.Equal(1, await LettersAsync(world, "order.paid"));
    }

    // The payment the order already has, reported again under a new event id. It is the same money; keeping it
    // as a second payment would invite the store to refund the only one there is.
    [Fact]
    public async Task The_payment_an_order_already_has_is_not_kept_as_a_second_one()
    {
        var world = await OrderAtTheGatewayAsync();

        using var paid = await NotifyAsync(world, PaymentResult.Paid);
        using var again = await NotifyAsync(world, PaymentResult.Paid);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (paid.StatusCode, again.StatusCode));
        Assert.Empty(await KeptAsync(world));
    }

    [Fact]
    public async Task A_late_payment_reported_twice_is_kept_once()
    {
        var world = await OrderAtTheGatewayAsync();
        await CancelAsync(world);

        using var first = await NotifyAsync(world, PaymentResult.Paid);
        using var again = await NotifyAsync(world, PaymentResult.Paid);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, again.StatusCode));
        Assert.Single(await KeptAsync(world));
    }

    // Nothing that is not a payment is kept: a refusal, and money held but never taken (D-140).
    [Fact]
    public async Task A_late_result_that_moved_no_money_keeps_nothing()
    {
        var world = await OrderAtTheGatewayAsync();
        await CancelAsync(world);

        using var held = await NotifyAsync(world, PaymentResult.Authorized);
        using var refused = await NotifyAsync(world, PaymentResult.Failed);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (held.StatusCode, refused.StatusCode));
        Assert.Empty(await KeptAsync(world));
    }

    // A store cancelling an order closes the order, not the gateway's payment: the gateway can still take money
    // for it, and an attempt closed here would never be asked about again (D-176, D-193).
    [Fact]
    public async Task Cancelling_an_order_leaves_the_gateway_s_attempt_for_the_gateway_to_answer()
    {
        var world = await OrderAtTheGatewayAsync();
        await CancelAsync(world);
        var afterCancelling = (await AttemptsAsync(world)).Single();

        using var paid = await NotifyAsync(world, PaymentResult.Paid);
        var afterPaying = (await AttemptsAsync(world)).Single();

        Assert.Equal(PaymentAttemptStatus.Started, afterCancelling.Status);
        Assert.Equal(PaymentAttemptStatus.Paid, afterPaying.Status);
    }

    // The gateway reports its own payments, and the sweep asks about any it has not heard of. A store that marked
    // one paid as well would make the gateway's report look like a second payment.
    [Fact]
    public async Task An_order_a_gateway_holds_open_is_not_confirmed_by_hand()
    {
        var world = await OrderAtTheGatewayAsync();

        using var confirmed = await world.Furniture.Admin.PostAsync(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/orders/{world.Number}/payment", null, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, confirmed.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderRowAsync(world)).Status);
        Assert.Equal(PaymentAttemptStatus.Started, (await AttemptsAsync(world)).Single().Status);
    }

    [Fact]
    public async Task The_order_and_the_store_s_list_both_show_it()
    {
        var world = await OrderAtTheGatewayAsync();
        await CancelAsync(world);
        using var paid = await NotifyAsync(world, PaymentResult.Paid);

        var order = await world.Furniture.Admin.GetFromJsonAsync<OrderWithKeptPayments>(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/orders/{world.Number}", CancellationToken);
        var open = await world.Furniture.Admin.GetFromJsonAsync<KeptPage>(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/unapplied-payments?status=Open", CancellationToken);
        var refunded = await world.Furniture.Admin.GetFromJsonAsync<KeptPage>(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/unapplied-payments?status=Refunded", CancellationToken);

        var shown = Assert.Single(order!.UnappliedPayments);
        Assert.Equal(("OrderCancelled", "Open", true), (shown.Reason, shown.Status, shown.CanRefund));
        Assert.Equal((1, world.Number), (open!.TotalCount, open.Items.Single().OrderNumber));
        Assert.Equal(0, refunded!.TotalCount);
    }

    // The number an alert watches. Every one of these is a refund somebody owes.
    [Fact]
    public async Task A_kept_payment_is_counted()
    {
        var world = await OrderAtTheGatewayAsync();
        await CancelAsync(world);
        var counted = 0L;

        using (var listener = new MeterListener())
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument is { Name: "shopforge.payments.unapplied", Meter.Name: "ShopForge" })
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref counted, measurement));
            listener.Start();

            using var paid = await NotifyAsync(world, PaymentResult.Paid);
            Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        }

        Assert.True(counted >= 1, "a kept payment should be counted");
    }

    private async Task CancelAsync(GatewayOrder world)
    {
        using var cancelled = await world.Furniture.Admin.PostAsync(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/orders/{world.Number}/cancel", null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
    }

    private async Task ExpireAsync(GatewayOrder world)
    {
        await using var scope = TestStores.CreateScope(_factory.Services, world.Furniture.Store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Order numbers restart per store, so the store has to be part of the match.
        await dbContext.Database.ExecuteSqlAsync(
            $"UPDATE orders.orders SET reservation_expires_at = now() - interval '1 hour' WHERE number = {world.Number} AND store_id = {world.Furniture.Store.StoreId}",
            CancellationToken);
    }

    private async Task<HttpResponseMessage> NotifyAsync(GatewayOrder world, PaymentResult result, string? reference = null)
    {
        using var client = _withGateway.CreateClient();

        return await client.PostAsync(
            $"/api/payments/{FakeGateway.ProviderKey}",
            FakeGateway.Notification(world.Furniture.Store.StoreId, world.Number, result, reference: reference),
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

    private Task<Order> OrderRowAsync(GatewayOrder world) =>
        _factory.QueryAsync(world.Furniture.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == world.Number, CancellationToken));

    private Task<List<UnappliedPayment>> KeptAsync(GatewayOrder world) =>
        _factory.QueryAsync(world.Furniture.Store, dbContext => dbContext.Set<UnappliedPayment>()
            .AsNoTracking()
            .Where(kept => kept.OrderNumber == world.Number)
            .ToListAsync(CancellationToken));

    private Task<List<PaymentAttempt>> AttemptsAsync(GatewayOrder world) =>
        _factory.QueryAsync(world.Furniture.Store, dbContext => dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .Where(attempt => attempt.OrderNumber == world.Number)
            .ToListAsync(CancellationToken));

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

    private sealed record OrderWithKeptPayments(List<KeptView> UnappliedPayments);

    private sealed record KeptPage(List<KeptView> Items, int TotalCount);

    private sealed record KeptView(Guid Id, string OrderNumber, string Reason, string Status, bool CanRefund);
}
