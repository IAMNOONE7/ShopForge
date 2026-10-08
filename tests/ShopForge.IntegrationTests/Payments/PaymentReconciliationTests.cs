using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Background;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// When nobody called: a payment whose notification was lost still finishes, because the shop goes and asks
// (D-176).
public sealed class PaymentReconciliationTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public PaymentReconciliationTests(ShopForgeApiFactory factory)
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

    [Fact]
    public async Task A_payment_nobody_reported_is_asked_about_and_finishes()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Says = Transaction(world, "PAID");

        var settled = await SweepAsync();

        Assert.Equal(1, settled);
        Assert.Equal(OrderStatus.Paid, (await OrderAsync(world)).Status);
        Assert.Equal(PaymentAttemptStatus.Paid, (await AttemptAsync(world)).Status);
        Assert.Equal(world.TransactionId, (await OrderAsync(world)).PaymentReference);
    }

    // A payment the gateway says was cancelled frees the stock, exactly as a push saying so would.
    [Fact]
    public async Task A_payment_the_gateway_says_was_cancelled_cancels_the_order()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Says = Transaction(world, "CANCELLED");

        await SweepAsync();

        Assert.Equal(OrderStatus.Cancelled, (await OrderAsync(world)).Status);
        Assert.Equal(PaymentAttemptStatus.Failed, (await AttemptAsync(world)).Status);
    }

    // The risk the slice named: both arrive, both go through the same path, one effect comes out.
    [Fact]
    public async Task A_sweep_and_a_late_push_about_the_same_state_leave_one_effect()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Says = Transaction(world, "PAID");

        var settled = await SweepAsync();
        using var pushed = await PushAsync(world);
        var sweptAgain = await SweepAsync();

        Assert.Equal(1, settled);
        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(0, sweptAgain);
        Assert.Equal(OrderStatus.Paid, (await OrderAsync(world)).Status);
        Assert.Equal(1, await PaymentEventsAsync(world));
        Assert.Equal(1, await LettersAsync(world, "order.paid"));
    }

    // An attempt still young is the shopper still on the gateway's page: asking about it is asking a question
    // nobody has had time to answer.
    [Fact]
    public async Task An_attempt_nobody_has_had_time_to_answer_is_not_asked_about()
    {
        var world = await StartedAtComgateAsync();
        _comgate.Says = Transaction(world, "PAID");

        var settled = await SweepAsync();

        Assert.Equal(0, settled);
        Assert.False(_comgate.AskedAbout);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
    }

    // A gateway that cannot be reached has not said the payment failed; it has said nothing.
    [Fact]
    public async Task A_gateway_that_cannot_answer_leaves_the_attempt_to_be_asked_again()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Fails = true;

        var settled = await SweepAsync();

        Assert.Equal(0, settled);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
        Assert.Equal(PaymentAttemptStatus.Started, (await AttemptAsync(world)).Status);

        _comgate.Fails = false;
        _comgate.Says = Transaction(world, "PAID");

        Assert.Equal(1, await SweepAsync());
        Assert.Equal(OrderStatus.Paid, (await OrderAsync(world)).Status);
    }

    [Fact]
    public async Task An_attempt_that_already_finished_is_not_asked_about_again()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Says = Transaction(world, "PAID");
        await SweepAsync();

        // Back-dated again, so what keeps it out of the sweep is having finished rather than having just
        // changed: without this the age filter alone would hide it and prove nothing.
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Forget();
        var settled = await SweepAsync();

        Assert.Equal(0, settled);
        Assert.False(_comgate.AskedAbout);
    }

    // The gateway answering with something that cannot be believed is not the gateway saying the payment
    // failed. A transaction for another order, or in the other environment, moves nothing — and the attempt
    // stays where it is rather than being cancelled on the strength of a bad answer (D-141).
    [Fact]
    public async Task An_answer_that_disagrees_with_what_was_sent_moves_nothing()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Says = Transaction(world, "PAID") with { ReferenceId = "somebody-elses-order" };

        var forAnotherOrder = await SweepAsync();

        _comgate.Says = Transaction(world, "PAID") with { Test = false };
        var fromTheOtherEnvironment = await SweepAsync();

        Assert.Equal(0, forAnotherOrder);
        Assert.Equal(0, fromTheOtherEnvironment);
        Assert.True(_comgate.AskedAbout);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
        Assert.Equal(PaymentAttemptStatus.Started, (await AttemptAsync(world)).Status);
    }

    // And a transaction the gateway has never heard of is the same kind of nothing.
    [Fact]
    public async Task A_transaction_the_gateway_does_not_know_moves_nothing()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Says = null;

        var settled = await SweepAsync();

        Assert.Equal(0, settled);
        Assert.True(_comgate.AskedAbout);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
    }

    // Past the give-up age nobody is waiting and the gateway decided long ago.
    [Fact]
    public async Task An_attempt_older_than_anybody_waits_is_left_alone()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.GivenUp + TimeSpan.FromDays(1));
        _comgate.Says = Transaction(world, "PAID");

        var settled = await SweepAsync();

        Assert.Equal(0, settled);
        Assert.False(_comgate.AskedAbout);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
    }

    // What the merchant can see, and what they cannot: a transaction id and a status, never a credential.
    [Fact]
    public async Task The_admin_shows_every_attempt_with_its_status_and_environment_and_no_secret()
    {
        var world = await StartedAtComgateAsync();
        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        _comgate.Says = Transaction(world, "PAID");
        await SweepAsync();

        var (body, detail) = await AdminOrderAsync(world);
        var attempt = detail.PaymentAttempts.Single();

        Assert.Equal("comgate", attempt.Provider);
        Assert.Equal(world.TransactionId, attempt.Reference);
        Assert.Equal("Paid", attempt.Status);
        Assert.Equal("test", attempt.Environment);
        Assert.Equal(world.TransactionId, detail.PaymentReference);
        Assert.NotNull(detail.PaidAt);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_store_with_no_gateway_connection_has_nothing_to_reconcile()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);

        var settled = await SweepAsync();

        Assert.Equal(0, settled);
        Assert.NotEqual(Guid.Empty, furniture.Store.StoreId);
    }

    private ComgateTransaction Transaction(ComgateOrder world, string status) =>
        new(world.TransactionId, status, Currency.Of("EUR").ToMinorUnits(world.Amount), "EUR", world.Number, Test: true);

    private Task<int> SweepAsync() =>
        _withComgate.Services.GetRequiredService<StoreCatchUp>().RunAsync(CancellationToken);

    // Back-dating the attempt is how a test says "this has been waiting", without a clock the whole host
    // would have to share.
    private async Task WaitedAsync(ComgateOrder world, TimeSpan howLong) =>
        await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE orders.payment_attempts
            SET started_at = started_at - {howLong}, changed_at = changed_at - {howLong}
            WHERE order_number = {world.Number}
            """,
            CancellationToken));

    private async Task<HttpResponseMessage> PushAsync(ComgateOrder world)
    {
        using var client = _withComgate.CreateClient();

        return await client.PostAsJsonAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new { merchant = world.MerchantId, transId = world.TransactionId },
            CancellationToken);
    }

    private async Task<(string Body, AdminOrderView Detail)> AdminOrderAsync(ComgateOrder world)
    {
        using var admin = await TestUsers.LoginAsync(_withComgate, await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId));
        using var response = await admin.GetAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/orders/{world.Number}", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        return (body, System.Text.Json.JsonSerializer.Deserialize<AdminOrderView>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
    }

    private async Task<ComgateOrder> StartedAtComgateAsync()
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

        using var shopper = new StorefrontApi(_withComgate, store.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var attempt = await _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == order.Number, CancellationToken));

        _comgate.Forget();

        return new ComgateOrder(store, merchant, order.Number, attempt.Reference!, attempt.Amount);
    }

    private Task<Order> OrderAsync(ComgateOrder world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == world.Number, CancellationToken));

    private Task<PaymentAttempt> AttemptAsync(ComgateOrder world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(attempt => attempt.OrderNumber == world.Number, CancellationToken));

    private Task<int> PaymentEventsAsync(ComgateOrder world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<PaymentEvent>()
            .AsNoTracking()
            .CountAsync(recorded => recorded.OrderNumber == world.Number, CancellationToken));

    private async Task<int> LettersAsync(ComgateOrder world, string type)
    {
        var payloads = await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {world.Store.Store.StoreId} AND type = {type}")
            .ToListAsync(CancellationToken));

        return payloads.Count(payload => payload.Contains(world.Number, StringComparison.Ordinal));
    }

    private sealed record ComgateOrder(FurnitureStore Store, string MerchantId, string Number, string TransactionId, decimal Amount);

    private sealed record AdminOrderView(
        string? PaymentReference,
        DateTimeOffset? PaidAt,
        List<AttemptView> PaymentAttempts);

    private sealed record AttemptView(string Provider, string? Reference, string Status, string? Environment);
}
