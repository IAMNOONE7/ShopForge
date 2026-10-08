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

// Comgate's push says "this merchant, this transaction"; everything else is read from Comgate and compared with
// what was sent. A push that disagrees moves nothing (D-141).
public sealed class ComgateNotificationTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public ComgateNotificationTests(ShopForgeApiFactory factory)
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
    public async Task A_payment_comgate_confirms_when_asked_pays_the_order()
    {
        var world = await PaidAtComgateAsync("PAID");

        var order = await OrderAsync(world);

        Assert.True(_comgate.AskedAbout);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(world.TransactionId, order.PaymentReference);
        Assert.Equal(PaymentAttemptStatus.Paid, await AttemptStatusAsync(world));
    }

    [Fact]
    public async Task A_payment_still_going_moves_nothing_and_one_that_is_cancelled_frees_the_stock()
    {
        var pending = await PaidAtComgateAsync("PENDING");
        var cancelled = await PaidAtComgateAsync("CANCELLED");

        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(pending)).Status);
        Assert.Equal(PaymentAttemptStatus.Pending, await AttemptStatusAsync(pending));
        Assert.Equal(OrderStatus.Cancelled, (await OrderAsync(cancelled)).Status);
        Assert.Equal(PaymentAttemptStatus.Failed, await AttemptStatusAsync(cancelled));
    }

    // Held money is not taken money, whatever the push says it is.
    [Fact]
    public async Task An_authorisation_leaves_the_order_waiting()
    {
        var world = await PaidAtComgateAsync("AUTHORIZED");

        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
        Assert.Equal(PaymentAttemptStatus.Authorized, await AttemptStatusAsync(world));
    }

    // Everything a forged or stale push could claim, checked against what we sent.
    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("order")]
    [InlineData("environment")]
    public async Task A_transaction_that_disagrees_with_what_was_sent_moves_nothing(string difference)
    {
        var world = await StartedAtComgateAsync();
        var honest = Transaction(world, "PAID");

        _comgate.Says = difference switch
        {
            "amount" => honest with { PriceInMinorUnits = honest.PriceInMinorUnits + 100 },
            "currency" => honest with { Currency = "USD" },
            "order" => honest with { ReferenceId = "2099-99999" },
            _ => honest with { Test = false },
        };

        using var pushed = await PushAsync(world);

        Assert.Equal(HttpStatusCode.BadRequest, pushed.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
    }

    [Fact]
    public async Task A_push_about_a_transaction_nobody_started_is_refused()
    {
        var world = await StartedAtComgateAsync();

        using var pushed = await PushAsync(world, "trans-nobody-started");

        Assert.Equal(HttpStatusCode.BadRequest, pushed.StatusCode);
        Assert.False(_comgate.AskedAbout);
    }

    // A provider retries until it is answered, so the same message twice must leave one effect.
    [Fact]
    public async Task The_same_message_twice_pays_once()
    {
        var world = await StartedAtComgateAsync();
        _comgate.Says = Transaction(world, "PAID");

        using var first = await PushAsync(world);
        using var again = await PushAsync(world);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, await LettersAsync(world, "order.paid"));
    }

    // Going pending and then paying is two things that both happened, and the second must not be mistaken for
    // a repeat of the first.
    [Fact]
    public async Task A_payment_that_was_pending_can_still_pay()
    {
        var world = await StartedAtComgateAsync();

        _comgate.Says = Transaction(world, "PENDING");
        using var waiting = await PushAsync(world);
        _comgate.Says = Transaction(world, "PAID");
        using var arrived = await PushAsync(world);

        Assert.Equal(HttpStatusCode.OK, waiting.StatusCode);
        Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
        Assert.Equal(OrderStatus.Paid, (await OrderAsync(world)).Status);
    }

    // A check that cannot be made is not a check that failed: Comgate must be told to come back.
    [Fact]
    public async Task A_gateway_that_will_not_answer_the_check_is_asked_to_try_again()
    {
        var world = await StartedAtComgateAsync();
        _comgate.Fails = true;

        try
        {
            using var pushed = await PushAsync(world);

            Assert.Equal(HttpStatusCode.InternalServerError, pushed.StatusCode);
            Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
        }
        finally
        {
            _comgate.Fails = false;
        }
    }

    // The merchant says which stores are in question and the transaction says which order, so a second store's
    // order is untouched whatever a push claims.
    [Fact]
    public async Task A_push_pays_the_order_of_the_store_whose_transaction_it_names()
    {
        var first = await StartedAtComgateAsync();
        var second = await StartedAtComgateAsync();
        _comgate.Says = Transaction(first, "PAID");

        using var pushed = await PushAsync(first);

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(OrderStatus.Paid, (await OrderAsync(first)).Status);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(second)).Status);
    }

    // A transaction id is Comgate's own and unique only within the account that holds it, so it is worth nothing
    // under someone else's merchant — including to a merchant who learned it and tried to spend it.
    [Fact]
    public async Task A_transaction_claimed_under_another_merchant_moves_nothing()
    {
        var world = await StartedAtComgateAsync();
        var other = await StartedAtComgateAsync();
        _comgate.Says = Transaction(world, "PAID");

        using var pushed = await PushAsync(other.MerchantId, world.TransactionId);

        Assert.Equal(HttpStatusCode.BadRequest, pushed.StatusCode);
        Assert.False(_comgate.AskedAbout);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(world)).Status);
    }

    // One company, two storefronts, one Comgate account: the merchant no longer picks the store out on its own,
    // and the transaction has to.
    [Fact]
    public async Task Two_stores_selling_under_one_account_are_still_told_apart()
    {
        var first = await StartedAtComgateAsync();
        var second = await StartedAtComgateAsync(first.MerchantId);
        _comgate.Says = Transaction(second, "PAID");

        using var pushed = await PushAsync(second);

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(OrderStatus.Paid, (await OrderAsync(second)).Status);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderAsync(first)).Status);
    }

    private ComgateTransaction Transaction(ComgateOrder world, string status) =>
        new(world.TransactionId, status, Currency.Of("EUR").ToMinorUnits(world.Amount), "EUR", world.Number, Test: true);

    private async Task<ComgateOrder> PaidAtComgateAsync(string status)
    {
        var world = await StartedAtComgateAsync();
        _comgate.Says = Transaction(world, status);

        using var pushed = await PushAsync(world);
        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);

        return world;
    }

    private async Task<HttpResponseMessage> PushAsync(ComgateOrder world, string? transactionId = null) =>
        await PushAsync(world.MerchantId, transactionId ?? world.TransactionId);

    private async Task<HttpResponseMessage> PushAsync(string merchantId, string transactionId)
    {
        using var client = _withComgate.CreateClient();

        return await client.PostAsJsonAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new { merchant = merchantId, transId = transactionId },
            CancellationToken);
    }

    private async Task<ComgateOrder> StartedAtComgateAsync(string? merchantId = null)
    {
        var merchant = merchantId ?? $"MERCHANT-{Guid.NewGuid():N}"[..20];
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

        return new ComgateOrder(store, merchant, order.Number, attempt.Reference!, attempt.Amount);
    }

    private Task<Order> OrderAsync(ComgateOrder world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == world.Number, CancellationToken));

    private async Task<PaymentAttemptStatus> AttemptStatusAsync(ComgateOrder world) =>
        (await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(attempt => attempt.OrderNumber == world.Number, CancellationToken))).Status;

    private async Task<int> LettersAsync(ComgateOrder world, string type)
    {
        var payloads = await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {world.Store.Store.StoreId} AND type = {type}")
            .ToListAsync(CancellationToken));

        return payloads.Count(payload => payload.Contains(world.Number, StringComparison.Ordinal));
    }

    private sealed record ComgateOrder(FurnitureStore Store, string MerchantId, string Number, string TransactionId, decimal Amount);
}
