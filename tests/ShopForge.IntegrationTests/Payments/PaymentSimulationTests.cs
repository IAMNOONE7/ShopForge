using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Background;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// The ways a payment goes wrong, each run to the end and judged by what is left in the database, not by the absence
// of an exception (Roadmap V2 §4.3, item 3; D-199): the order, its attempt, the answers recorded, any money kept to
// give back, the stock, and the letters the shopper was sent. Comgate is the gateway; the fake answers what each
// scenario needs it to.
public sealed class PaymentSimulationTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    // Every order in these scenarios is for two chairs.
    private const int Bought = 2;

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly InMemorySecrets _secrets = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public PaymentSimulationTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withComgate = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IComgatePayments>(_comgate);
            services.AddSingleton<ISecretStore>(_secrets);
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withComgate.Dispose();

    // Comgate retries until it is answered, so the same payment is told more than once.
    [Fact]
    public async Task Duplicate_a_payment_told_twice_is_paid_once()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");

        await PushAsync(world);
        await PushAsync(world);

        Assert.Equal(Paid(world, events: 1), await EndStateAsync(world));
    }

    // A notification carries no state of its own, so an old one replayed after the store has refunded the order is
    // only a reason to ask again, and the answer has been heard before.
    [Fact]
    public async Task Replayed_a_notification_after_the_order_was_refunded_changes_nothing()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        await PushAsync(world);
        await RefundAsync(world);

        await PushAsync(world);

        Assert.Equal(
            Paid(world, events: 1) with { Order = OrderStatus.Refunded, OnHandChange = 0, Refund = "refunded", RefundCalls = 1 },
            await EndStateAsync(world));
    }

    // A late answer about an earlier state — a slow notification, a gateway that answered from a stale replica — is
    // recorded, and moves nothing back.
    [Fact]
    public async Task Out_of_order_a_pending_answer_after_the_payment_moves_nothing_back()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        await PushAsync(world);
        Answer(world, "PENDING");

        await PushAsync(world);

        Assert.Equal(Paid(world, events: 2), await EndStateAsync(world));
    }

    // The notification never came. The sweep asks once the attempt has been quiet long enough.
    [Fact]
    public async Task Missing_then_swept_a_payment_nobody_reported_is_found()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        await QuietAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));

        await SweepAsync(world);

        Assert.Equal(Paid(world, events: 1), await EndStateAsync(world));
    }

    // The shopper paid in the last minutes and the notification was lost. The order's time runs out, and before it
    // is let go Comgate is asked: the shopper is owed the order, not a refund (D-199).
    [Fact]
    public async Task Delayed_beyond_expiry_a_payment_made_in_time_confirms_the_order()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        await RunOutAsync(world, TimeSpan.FromMinutes(1));

        await ExpireAsync(world);

        Assert.Equal(Paid(world, events: 1), await EndStateAsync(world));
    }

    // Comgate still had the payment open when the order ran out, so the order was let go; the money came afterwards
    // and is kept to give back (D-193).
    [Fact]
    public async Task Delayed_beyond_expiry_a_payment_made_after_the_order_was_let_go_is_kept()
    {
        var world = await OrderedAsync();
        Answer(world, "PENDING");
        await RunOutAsync(world, TimeSpan.FromMinutes(1));
        await ExpireAsync(world);
        Answer(world, "PAID");

        await PushAsync(world);

        Assert.Equal(
            Cancelled(world, events: 2) with { Attempt = PaymentAttemptStatus.Paid, Kept = "OrderCancelled/Open" },
            await EndStateAsync(world));
    }

    // Comgate took too long to answer the check a notification asks for. It is told to try again, and the next
    // notification finds the payment.
    [Fact]
    public async Task Provider_timeout_then_success_the_notification_is_retried_and_pays()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        _comgate.LookupsTimingOut = 1;

        await PushAsync(world, HttpStatusCode.InternalServerError);
        var waiting = await EndStateAsync(world);
        await PushAsync(world);

        Assert.Equal(Waiting(world), waiting);
        Assert.Equal(Paid(world, events: 1), await EndStateAsync(world));
    }

    // The same silence at the moment an order runs out: the order is kept a little longer rather than let go, and the
    // next sweep confirms it.
    [Fact]
    public async Task Provider_timeout_then_success_at_expiry_keeps_the_order_and_then_confirms_it()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        await RunOutAsync(world, TimeSpan.FromMinutes(1));
        _comgate.LookupsTimingOut = 1;

        await ExpireAsync(world);
        var kept = await EndStateAsync(world);
        await ExpireAsync(world);

        Assert.Equal(Waiting(world), kept);
        Assert.Equal(Paid(world, events: 1), await EndStateAsync(world));
    }

    [Fact]
    public async Task Provider_timeout_then_failure_the_retried_notification_cancels_and_frees_the_stock()
    {
        var world = await OrderedAsync();
        Answer(world, "CANCELLED");
        _comgate.LookupsTimingOut = 1;

        await PushAsync(world, HttpStatusCode.InternalServerError);
        await PushAsync(world);

        Assert.Equal(Cancelled(world, events: 1), await EndStateAsync(world));
    }

    // A gateway silent for longer than the grace does not hold the stock: the order is let go, and a payment that
    // turns up later is kept to give back.
    [Fact]
    public async Task Provider_timeout_beyond_the_grace_lets_the_order_go_and_keeps_late_money()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        await RunOutAsync(world, ExpiredOrders.AskingGrace + TimeSpan.FromMinutes(1));
        _comgate.LookupsTimingOut = 1;

        await ExpireAsync(world);
        await PushAsync(world);

        Assert.Equal(
            Cancelled(world, events: 1) with { Attempt = PaymentAttemptStatus.Paid, Kept = "OrderCancelled/Open" },
            await EndStateAsync(world));
    }

    // Comgate gave the money back and the answer saying so was lost. The return is booked, the refund is left
    // pending for somebody to check, and pressing again sends nothing (D-194).
    [Fact]
    public async Task Ambiguous_refund_a_refund_whose_answer_was_lost_is_never_sent_again()
    {
        var world = await OrderedAsync();
        Answer(world, "PAID");
        await PushAsync(world);
        _comgate.RefundMovesButGoesUnanswered = true;

        await RefundAsync(world);
        using var admin = await AdminAsync(world);
        using var again = await admin.PostAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/orders/{world.Number}/refund", null, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(
            Paid(world, events: 1) with { Order = OrderStatus.Refunded, OnHandChange = 0, Refund = "pending", RefundCalls = 1 },
            await EndStateAsync(world));
    }

    private static EndState Paid(ComgateOrder world, int events) =>
        new(OrderStatus.Paid, world.TransactionId, PaymentAttemptStatus.Paid, events, "none", -Bought, 0, 1, 0, "none", 0);

    private static EndState Cancelled(ComgateOrder world, int events) =>
        new(OrderStatus.Cancelled, null, PaymentAttemptStatus.Failed, events, "none", 0, 0, 0, 1, "none", 0);

    private static EndState Waiting(ComgateOrder world) =>
        new(OrderStatus.AwaitingPayment, null, PaymentAttemptStatus.Started, 0, "none", 0, Bought, 0, 0, "none", 0);

    private void Answer(ComgateOrder world, string status) =>
        _comgate.Answers[world.TransactionId] = new ComgateTransaction(
            world.TransactionId, status, Currency.Of("EUR").ToMinorUnits(world.Amount), "EUR", world.Number, Test: true);

    private async Task PushAsync(ComgateOrder world, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var client = _withComgate.CreateClient();
        using var pushed = await client.PostAsJsonAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new { merchant = world.MerchantId, transId = world.TransactionId },
            CancellationToken);

        Assert.Equal(expected, pushed.StatusCode);
    }

    private async Task RefundAsync(ComgateOrder world)
    {
        using var admin = await AdminAsync(world);
        using var refunded = await admin.PostAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/orders/{world.Number}/refund", null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
    }

    // Signed in on the host that talks to the fake Comgate: the fixture's own client would give money back through
    // a gateway nobody connected.
    private async Task<HttpClient> AdminAsync(ComgateOrder world) =>
        await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId));

    private async Task SweepAsync(ComgateOrder world)
    {
        await using var scope = TestStores.CreateScope(_withComgate.Services, world.Store.Store);

        await scope.ServiceProvider.GetServices<IStoreCatchUp>().OfType<PaymentReconciliation>().Single().RunAsync(CancellationToken);
    }

    private Task<int> ExpireAsync(ComgateOrder world) =>
        _withComgate.Services.GetRequiredService<ExpiredOrders>().SweepStoreAsync(
            new ShopForge.Shared.Stores.StoreReference(world.Store.Store.StoreId, world.Store.Store.TenantId), CancellationToken);

    // Back-dating is how a test says "this has been waiting", without a clock the whole host would have to share.
    private async Task QuietAsync(ComgateOrder world, TimeSpan howLong) =>
        await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE orders.payment_attempts
            SET started_at = started_at - {howLong}, changed_at = changed_at - {howLong}
            WHERE order_number = {world.Number} AND store_id = {world.Store.Store.StoreId}
            """,
            CancellationToken));

    private async Task RunOutAsync(ComgateOrder world, TimeSpan ago) =>
        await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE orders.orders SET reservation_expires_at = {DateTimeOffset.UtcNow - ago}
            WHERE number = {world.Number} AND store_id = {world.Store.Store.StoreId}
            """,
            CancellationToken));

    private async Task<EndState> EndStateAsync(ComgateOrder world)
    {
        var store = world.Store.Store;
        var order = await _factory.QueryAsync(store, dbContext => dbContext.Set<Order>().AsNoTracking()
            .SingleAsync(candidate => candidate.Number == world.Number, CancellationToken));
        var attempt = await _factory.QueryAsync(store, dbContext => dbContext.Set<PaymentAttempt>().AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == world.Number, CancellationToken));
        var events = await _factory.QueryAsync(store, dbContext => dbContext.Set<PaymentEvent>().AsNoTracking()
            .CountAsync(candidate => candidate.OrderNumber == world.Number, CancellationToken));
        var kept = await _factory.QueryAsync(store, dbContext => dbContext.Set<UnappliedPayment>().AsNoTracking()
            .Where(candidate => candidate.OrderNumber == world.Number)
            .Select(candidate => candidate.Reason + "/" + candidate.Status)
            .ToListAsync(CancellationToken));
        var refund = await _factory.QueryAsync(store, dbContext => dbContext.Set<OrderReturn>().AsNoTracking()
            .Where(candidate => candidate.OrderNumber == world.Number)
            .Select(candidate => candidate.Refund)
            .ToListAsync(CancellationToken));
        var stock = await StockAsync(world);

        return new EndState(
            order.Status,
            order.PaymentReference,
            attempt.Status,
            events,
            kept.Count == 0 ? "none" : string.Join(",", kept),
            stock.OnHand - world.OnHandBefore,
            stock.Reserved,
            await LettersAsync(world, "order.paid"),
            await LettersAsync(world, "order.cancelled"),
            refund.Count == 0 ? "none" : string.Join(",", refund.Select(state => state?.ToString().ToLowerInvariant() ?? "none")),
            _comgate.Refunded.Count(call => call.TransactionId == world.TransactionId));
    }

    private async Task<StockView> StockAsync(ComgateOrder world)
    {
        var stock = await world.Store.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.Single(item => item.VariantId == world.VariantId);
    }

    private async Task<int> LettersAsync(ComgateOrder world, string type)
    {
        var payloads = await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {world.Store.Store.StoreId} AND type = {type}")
            .ToListAsync(CancellationToken));

        return payloads.Count(payload => payload.Contains(world.Number, StringComparison.Ordinal));
    }

    private async Task<ComgateOrder> OrderedAsync()
    {
        var merchant = $"MERCHANT-{Guid.NewGuid():N}"[..20];
        var store = await FurnitureStore.CreateAsync(_factory);
        using var admin = await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, store.Store.TenantId));

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

        var variantId = await store.Admin.DefaultVariantIdAsync(store.ProductIds["oak-chair"]);
        var before = (await store.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken))!
            .Single(item => item.VariantId == variantId).OnHand;

        using var shopper = new StorefrontApi(_withComgate, store.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = Bought });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var attempt = await _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == order.Number, CancellationToken));

        return new ComgateOrder(store, merchant, order.Number, attempt.Reference!, attempt.Amount, variantId, before);
    }

    // Everything a scenario is judged by, compared whole so that a scenario names the state it ends in.
    private sealed record EndState(
        OrderStatus Order,
        string? PaymentReference,
        PaymentAttemptStatus Attempt,
        int Events,
        string Kept,
        int OnHandChange,
        int Reserved,
        int PaidLetters,
        int CancelledLetters,
        string Refund,
        int RefundCalls);

    private sealed record ComgateOrder(
        FurnitureStore Store, string MerchantId, string Number, string TransactionId, decimal Amount, Guid VariantId, int OnHandBefore);

    private sealed record StockView(Guid VariantId, int OnHand, int Reserved, int Available);
}
