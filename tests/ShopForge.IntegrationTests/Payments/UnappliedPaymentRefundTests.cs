using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Background;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// Giving back money an order could not take, in one action, through the gateway that took it. A refund is the one
// operation here that can lose money twice over, so it is claimed before it is sent, a refusal leaves it ready to
// try again, and a refund nobody heard back about is never sent a second time (D-193).
public sealed class UnappliedPaymentRefundTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public UnappliedPaymentRefundTests(ShopForgeApiFactory factory)
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
    public async Task A_payment_after_cancellation_goes_back_through_comgate_in_one_action()
    {
        var world = await PaidAfterCancellingAsync();

        using var refunded = await ActAsync(world, "refund");
        var kept = await KeptAsync(world);
        var given = Assert.Single(_comgate.Refunded);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal(UnappliedPaymentStatus.Refunded, kept.Status);
        Assert.Equal((world.TransactionId, Currency.Of("EUR").ToMinorUnits(world.Total), true), given);
        Assert.Equal(1, await LettersAsync(world, "payment.unapplied-refunded"));

        // The money was never the order's, so there is nothing to credit: no invoice was issued and none is
        // cancelled, and the order stays as it was.
        Assert.Equal(OrderStatus.Cancelled, (await OrderRowAsync(world)).Status);
        Assert.Equal(0, await DocumentsAsync(world));
    }

    [Fact]
    public async Task Pressing_refund_twice_gives_the_money_back_once()
    {
        var world = await PaidAfterCancellingAsync();

        using var first = await ActAsync(world, "refund");
        using var again = await ActAsync(world, "refund");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (first.StatusCode, again.StatusCode));
        Assert.Single(_comgate.Refunded);
    }

    [Fact]
    public async Task Two_refunds_at_the_same_moment_give_the_money_back_once()
    {
        var world = await PaidAfterCancellingAsync();

        var answers = await Task.WhenAll(ActAsync(world, "refund"), ActAsync(world, "refund"));
        var statuses = answers.Select(answer => answer.StatusCode).Order().ToList();

        foreach (var answer in answers)
        {
            answer.Dispose();
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        Assert.Single(_comgate.Refunded);
    }

    // Refused before anything was sent, so nothing moved: the payment is open again and the reason is shown.
    [Fact]
    public async Task A_refund_comgate_refuses_can_be_tried_again()
    {
        var world = await PaidAfterCancellingAsync();
        _comgate.Says = Transaction(world, "CANCELLED");

        using var refused = await ActAsync(world, "refund");
        var afterRefusal = await KeptAsync(world);
        var problem = await refused.Content.ReadFromJsonAsync<Problem>(CancellationToken);

        _comgate.Says = Transaction(world, "PAID");
        using var retried = await ActAsync(world, "refund");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("not a payment to refund", problem!.Detail, StringComparison.Ordinal);
        Assert.Equal(UnappliedPaymentStatus.Open, afterRefusal.Status);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Single(_comgate.Refunded);
    }

    // The request went out and no answer came back. The money may have moved, so it is not sent again: it waits
    // for somebody to look at Comgate and close it by hand.
    [Fact]
    public async Task A_refund_nobody_heard_back_about_is_not_sent_again()
    {
        var world = await PaidAfterCancellingAsync();
        _comgate.RefundGoesUnanswered = true;

        using var unanswered = await ActAsync(world, "refund");
        var afterSilence = await KeptAsync(world);

        _comgate.RefundGoesUnanswered = false;
        using var again = await ActAsync(world, "refund");
        using var closed = await ActAsync(world, "close", new { Note = "Comgate shows the refund went through on the 12th." });
        var afterClosing = await KeptAsync(world);

        Assert.Equal(HttpStatusCode.BadGateway, unanswered.StatusCode);
        Assert.Equal(UnappliedPaymentStatus.Refunding, afterSilence.Status);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Empty(_comgate.Refunded);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(
            (UnappliedPaymentStatus.ClosedByHand, "Comgate shows the refund went through on the 12th."),
            (afterClosing.Status, afterClosing.Note));
    }

    [Fact]
    public async Task Closing_by_hand_says_how_and_ends_it()
    {
        var world = await PaidAfterCancellingAsync();

        using var silent = await ActAsync(world, "close", new { Note = "   " });
        using var rambling = await ActAsync(world, "close", new { Note = new string('x', UnappliedPayment.MaxNoteLength + 1) });
        using var closed = await ActAsync(world, "close", new { Note = "Refunded at the Comgate portal." });
        using var refundedAfterwards = await ActAsync(world, "refund");
        using var closedAgain = await ActAsync(world, "close", new { Note = "Again." });

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (silent.StatusCode, rambling.StatusCode));
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (refundedAfterwards.StatusCode, closedAgain.StatusCode));
        Assert.Empty(_comgate.Refunded);
        Assert.Equal("Refunded at the Comgate portal.", (await KeptAsync(world)).Note);
    }

    // The same number a failed return refund is counted on: money owed that nobody has confirmed went back (D-194).
    [Fact]
    public async Task A_refund_refused_or_unanswered_is_counted_by_why()
    {
        var refused = await PaidAfterCancellingAsync();
        var unanswered = await PaidAfterCancellingAsync();
        var reasons = new List<string>();

        using (var listener = new MeterListener())
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument is { Name: "shopforge.refunds.failed", Meter.Name: "ShopForge" })
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "reason")
                    {
                        lock (reasons)
                        {
                            reasons.Add((string)tag.Value!);
                        }
                    }
                }
            });
            listener.Start();

            _comgate.Says = Transaction(refused, "CANCELLED");
            using var refusal = await ActAsync(refused, "refund");
            _comgate.Says = Transaction(unanswered, "PAID");
            _comgate.RefundGoesUnanswered = true;
            using var silence = await ActAsync(unanswered, "refund");

            Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.BadGateway), (refusal.StatusCode, silence.StatusCode));
        }

        Assert.Contains("refused", reasons);
        Assert.Contains("unanswered", reasons);
    }

    // Giving money back is the store's decision, the same as refunding an order is.
    [Fact]
    public async Task Somebody_in_support_cannot_give_the_money_back()
    {
        var world = await PaidAfterCancellingAsync();
        var support = await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId, TenantRole.Support);
        using var supportClient = await TestUsers.LoginAsync(_withComgate, support);

        using var refunded = await supportClient.PostAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/unapplied-payments/{world.KeptId}/refund", null, CancellationToken);
        using var closed = await supportClient.PostAsJsonAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/unapplied-payments/{world.KeptId}/close", new { Note = "Mine now." }, CancellationToken);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (refunded.StatusCode, closed.StatusCode));
        Assert.Equal(UnappliedPaymentStatus.Open, (await KeptAsync(world)).Status);
        Assert.Empty(_comgate.Refunded);
    }

    // The sweep finds the payment nobody reported. A store that cancelled the order left the attempt for Comgate
    // to answer, so the sweep still asks — and what it hears is kept, not dropped (D-176, D-193).
    [Fact]
    public async Task A_payment_nobody_reported_is_found_by_the_sweep_and_kept()
    {
        var world = await PlacedAtComgateAsync();
        await CancelAsync(world);
        _comgate.Says = Transaction(world, "PAID");

        await WaitedAsync(world, PaymentReconciliation.Unanswered + TimeSpan.FromMinutes(1));
        await _withComgate.Services.GetRequiredService<StoreCatchUp>().RunAsync(CancellationToken);
        var kept = await KeptAsync(world);

        Assert.Equal((UnappliedReason.OrderCancelled, world.TransactionId), (kept.Reason, kept.Reference));
    }

    // A shopper who comes back and tries again makes a second attempt (D-141). What is kept, and what a refund
    // later gives back, is what the attempt Comgate named asked for, not whatever was tried last.
    [Fact]
    public async Task What_is_kept_is_what_the_named_attempt_asked_for()
    {
        var world = await PlacedAtComgateAsync();
        await _factory.QueryAsync(world.Store.Store, async dbContext =>
        {
            dbContext.Add(new PaymentAttempt(
                world.Store.Store.StoreId,
                world.Number,
                ComgatePaymentProvider.ProviderKey,
                $"trans-later-{Guid.NewGuid():N}",
                world.Total + 100m,
                Currency.Of("EUR"),
                "https://pay.comgate.test/later",
                DateTimeOffset.UtcNow.AddMinutes(1)));

            return await dbContext.SaveChangesAsync(CancellationToken);
        });
        await CancelAsync(world);

        var kept = await PushedAsync(world);
        var attempts = await _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .Where(attempt => attempt.OrderNumber == world.Number)
            .ToDictionaryAsync(attempt => attempt.Reference!, attempt => attempt.Status, CancellationToken));

        Assert.Equal(world.Total, kept.Amount);
        Assert.Equal(PaymentAttemptStatus.Paid, attempts[world.TransactionId]);
        Assert.Equal(PaymentAttemptStatus.Started, attempts.Single(attempt => attempt.Key != world.TransactionId).Value);
    }

    private async Task<UnappliedPayment> PushedAsync(ComgateWorld world)
    {
        _comgate.Says = Transaction(world, "PAID");
        using var client = _withComgate.CreateClient();
        using var pushed = await client.PostAsJsonAsync(
            $"/api/payments/{ComgatePaymentProvider.ProviderKey}",
            new { merchant = world.Merchant, transId = world.TransactionId },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);

        return await KeptAsync(world);
    }

    private Task<HttpResponseMessage> ActAsync(ComgateWorld world, string action, object? body = null) =>
        body is null
            ? world.Admin.PostAsync($"/api/admin/stores/{world.Store.Store.StoreId}/unapplied-payments/{world.KeptId}/{action}", null, CancellationToken)
            : world.Admin.PostAsJsonAsync($"/api/admin/stores/{world.Store.Store.StoreId}/unapplied-payments/{world.KeptId}/{action}", body, CancellationToken);

    // A Comgate order the store cancelled, then paid at Comgate anyway, and pushed.
    private async Task<ComgateWorld> PaidAfterCancellingAsync()
    {
        var world = await PlacedAtComgateAsync();
        await CancelAsync(world);

        return world with { KeptId = (await PushedAsync(world)).Id };
    }

    private async Task<ComgateWorld> PlacedAtComgateAsync()
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

        using var buyer = new StorefrontApi(_withComgate, store.Store);
        using var added = await buyer.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var attempt = await _factory.QueryAsync(store.Store, dbContext => dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == order.Number, CancellationToken));

        return new ComgateWorld(store, admin, merchant, order.Number, attempt.Reference!, attempt.Amount, Guid.Empty);
    }

    // Back-dating the attempt is how a test says "this has been waiting", without a clock the whole host would
    // have to share. Order numbers restart per store, so the store is part of the match.
    private async Task WaitedAsync(ComgateWorld world, TimeSpan howLong) =>
        await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE orders.payment_attempts
            SET started_at = started_at - {howLong}, changed_at = changed_at - {howLong}
            WHERE order_number = {world.Number} AND store_id = {world.Store.Store.StoreId}
            """,
            CancellationToken));

    private static async Task CancelAsync(ComgateWorld world)
    {
        using var cancelled = await world.Admin.PostAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/orders/{world.Number}/cancel", null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
    }

    private static ComgateTransaction Transaction(ComgateWorld world, string status) =>
        new(world.TransactionId, status, Currency.Of("EUR").ToMinorUnits(world.Total), "EUR", world.Number, Test: true);

    private Task<UnappliedPayment> KeptAsync(ComgateWorld world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<UnappliedPayment>()
            .AsNoTracking()
            .SingleAsync(kept => kept.OrderNumber == world.Number, CancellationToken));

    private Task<Order> OrderRowAsync(ComgateWorld world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == world.Number, CancellationToken));

    private async Task<int> DocumentsAsync(ComgateWorld world)
    {
        var order = await world.Admin.GetFromJsonAsync<OrderDocuments>(
            $"/api/admin/stores/{world.Store.Store.StoreId}/orders/{world.Number}", CancellationToken);

        return order!.Documents.Count;
    }

    private async Task<int> LettersAsync(ComgateWorld world, string type)
    {
        var payloads = await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {world.Store.Store.StoreId} AND type = {type}")
            .ToListAsync(CancellationToken));

        return payloads.Count(payload => payload.Contains(world.Number, StringComparison.Ordinal));
    }

    private sealed record ComgateWorld(
        FurnitureStore Store, HttpClient Admin, string Merchant, string Number, string TransactionId, decimal Total, Guid KeptId);

    private sealed record Problem(string? Title, string? Detail);

    private sealed record OrderDocuments(List<object> Documents);
}
