using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShopForge.Access.Domain;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.IntegrationTests.Payments;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Invoicing;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Returns;

// A refund used to be sent from inside the receipt. A refusal threw and rolled the receipt back, so the parcel could
// never be booked; worse, a refund that moved the money and lost its answer rolled back too, and pressing again sent
// the money a second time. The receipt is now booked first and committed, and the refund follows it with a state
// of its own (D-194).
public sealed class RefundStateTests : IDisposable
{
    private const string Secret = "the-merchants-comgate-secret";

    private readonly ShopForgeApiFactory _factory;
    private readonly FakeComgate _comgate = new();
    private readonly WebApplicationFactory<Program> _withComgate;

    public RefundStateTests(ShopForgeApiFactory factory)
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
    public async Task A_refund_comgate_refuses_leaves_the_parcel_booked_and_says_why()
    {
        var world = await PaidAtComgateAsync(withAccount: true);
        var returnId = await AcceptedReturnAsync(world);
        var onHandBefore = await OnHandAsync(world);
        _comgate.Says = Transaction(world, "CANCELLED");

        using var received = await AdminAsync(world, $"returns/{returnId}/receive");
        var view = await received.Content.ReadFromJsonAsync<ReturnView>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        Assert.Equal(("Received", "Failed"), (view!.Status, view.Refund));
        Assert.Contains("not a payment to refund", view.RefundProblem, StringComparison.Ordinal);

        // The goods are back and the paperwork is issued: the parcel arrived whatever the gateway said.
        Assert.Equal(onHandBefore + 1, await OnHandAsync(world));
        Assert.Equal(1, await CreditNotesAsync(world));
        Assert.Equal(world.Total, (await OrderRowAsync(world)).RefundedTotal);

        // The customer is not told money is on its way that is not.
        Assert.Empty(_comgate.Refunded);
        Assert.Equal(0, await LettersAsync(world, "return.refunded"));
    }

    [Fact]
    public async Task A_refused_refund_goes_through_when_tried_again()
    {
        var world = await PaidAtComgateAsync(withAccount: true);
        var returnId = await AcceptedReturnAsync(world);
        _comgate.Says = Transaction(world, "CANCELLED");
        using var received = await AdminAsync(world, $"returns/{returnId}/receive");

        _comgate.Says = Transaction(world, "PAID");
        using var again = await AdminAsync(world, $"returns/{returnId}/refund");
        var view = await again.Content.ReadFromJsonAsync<ReturnView>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(("Refunded", (string?)null), (view!.Refund, view.RefundProblem));
        Assert.Equal((world.TransactionId, Currency.Of("EUR").ToMinorUnits(world.Total), true), Assert.Single(_comgate.Refunded));
        Assert.Equal(1, await LettersAsync(world, "return.refunded"));
    }

    // The defect that lost money. Comgate gave the money back and the answer was lost; the receipt used to roll
    // back with it, so the order still looked refundable and pressing again sent the money a second time.
    [Fact]
    public async Task A_refund_whose_answer_was_lost_is_never_sent_twice()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.RefundMovesButGoesUnanswered = true;

        using var refunded = await AdminAsync(world, $"orders/{world.Number}/refund");
        var order = await refunded.Content.ReadFromJsonAsync<OrderWithReturns>(CancellationToken);
        var kept = Assert.Single(order!.Returns);

        _comgate.RefundMovesButGoesUnanswered = false;
        using var refundedAgain = await AdminAsync(world, $"orders/{world.Number}/refund");
        using var triedAgain = await AdminAsync(world, $"returns/{kept.Id}/refund");

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal("Pending", kept.Refund);
        Assert.Contains("did not say whether the money went back", kept.RefundProblem, StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (refundedAgain.StatusCode, triedAgain.StatusCode));
        Assert.Single(_comgate.Refunded);
        Assert.Equal(1, await CreditNotesAsync(world));
    }

    [Fact]
    public async Task An_unconfirmed_refund_is_closed_by_hand_once_somebody_has_checked()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.RefundGoesUnanswered = true;
        var returnId = await RefundedOrderAsync(world);

        using var closed = await AdminAsync(world, $"returns/{returnId}/refund/close", new { Note = "Comgate shows it refunded on the 12th." });
        var view = await closed.Content.ReadFromJsonAsync<ReturnView>(CancellationToken);
        using var closedAgain = await AdminAsync(world, $"returns/{returnId}/refund/close", new { Note = "Again." });

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(("ClosedByHand", "Comgate shows it refunded on the 12th."), (view!.Refund, view.RefundNote));
        Assert.Equal(HttpStatusCode.Conflict, closedAgain.StatusCode);
        Assert.Equal(1, await LettersAsync(world, "return.refunded"));
        Assert.Equal(1, await AuditedAsync(world, "return.refund-closed"));
    }

    [Fact]
    public async Task Closing_by_hand_says_how()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.Says = Transaction(world, "CANCELLED");
        var returnId = await RefundedOrderAsync(world);

        using var silent = await AdminAsync(world, $"returns/{returnId}/refund/close", new { Note = " " });
        using var rambling = await AdminAsync(world, $"returns/{returnId}/refund/close", new { Note = new string('x', OrderReturn.MaxRefundTextLength + 1) });

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (silent.StatusCode, rambling.StatusCode));
        Assert.Equal(RefundStatus.Failed, (await ReturnRowAsync(world, returnId)).Refund);
    }

    // Settled is settled: neither tried again nor closed over.
    [Fact]
    public async Task A_refund_that_went_through_is_left_alone()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        var returnId = await RefundedOrderAsync(world);

        using var triedAgain = await AdminAsync(world, $"returns/{returnId}/refund");
        using var closed = await AdminAsync(world, $"returns/{returnId}/refund/close", new { Note = "Just in case." });

        Assert.Equal(RefundStatus.Refunded, (await ReturnRowAsync(world, returnId)).Refund);
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (triedAgain.StatusCode, closed.StatusCode));
        Assert.Single(_comgate.Refunded);
        Assert.Equal(1, await LettersAsync(world, "return.refunded"));
    }

    // A colleague closes the refund by hand while Comgate is still working on it. The money moves once, the customer
    // hears once, and what the colleague wrote stands. It also shows Comgate is asked with nothing locked: were the
    // receipt still open around the call, closing it would wait for a call that waits for it.
    [Fact]
    public async Task Closing_by_hand_while_comgate_is_still_answering_keeps_one_refund_and_one_letter()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.WhileRefunding = async () =>
        {
            var pending = await _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<OrderReturn>()
                .AsNoTracking()
                .SingleAsync(orderReturn => orderReturn.OrderNumber == world.Number, CancellationToken));
            using var closed = await AdminAsync(world, $"returns/{pending.Id}/refund/close", new { Note = "Refunded at the Comgate portal." });
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        };

        var returnId = await RefundedOrderAsync(world);
        var stored = await ReturnRowAsync(world, returnId);

        Assert.Equal((RefundStatus.ClosedByHand, "Refunded at the Comgate portal."), (stored.Refund, stored.RefundNote));
        Assert.Single(_comgate.Refunded);
        Assert.Equal(1, await LettersAsync(world, "return.refunded"));
    }

    // A gateway the deployment no longer has cannot be asked, and saying so is a refusal with a reason — not an
    // error that leaves the refund pending for ever.
    [Fact]
    public async Task A_gateway_this_deployment_cannot_refund_through_is_a_refusal_with_a_reason()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.Says = Transaction(world, "CANCELLED");
        var returnId = await RefundedOrderAsync(world);
        await using var withoutRefunds = _withComgate.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.RemoveAll<IPaymentRefunds>()));
        using var admin = await TestUsers.LoginAsync(withoutRefunds, await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId));

        using var again = await admin.PostAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/returns/{returnId}/refund", null, CancellationToken);
        var stored = await ReturnRowAsync(world, returnId);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(RefundStatus.Failed, stored.Refund);
        Assert.Contains("cannot give money back through comgate", stored.RefundProblem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_tries_at_the_same_moment_send_the_money_once()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.Says = Transaction(world, "CANCELLED");
        var returnId = await RefundedOrderAsync(world);
        _comgate.Says = Transaction(world, "PAID");

        var answers = await Task.WhenAll(AdminAsync(world, $"returns/{returnId}/refund"), AdminAsync(world, $"returns/{returnId}/refund"));
        var statuses = answers.Select(answer => answer.StatusCode).Order().ToList();

        foreach (var answer in answers)
        {
            answer.Dispose();
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        Assert.Single(_comgate.Refunded);
    }

    // Bank transfer and the like: no gateway to ask, so the store gives the money back itself, as it always did.
    [Fact]
    public async Task A_store_settling_its_own_payments_gives_the_money_back_itself()
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        using var buyer = new StorefrontApi(_factory, furniture.Store);
        using var added = await buyer.PostAsync("/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        await WaitForInvoiceAsync(furniture, order.Number);

        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        var returned = Assert.Single((await refunded.Content.ReadFromJsonAsync<OrderWithReturns>(CancellationToken))!.Returns);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal("ByStore", returned.Refund);
        Assert.Empty(_comgate.Refunded);
    }

    [Fact]
    public async Task The_list_finds_the_customers_still_owed()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.Says = Transaction(world, "CANCELLED");
        var returnId = await RefundedOrderAsync(world);

        var owed = await world.Admin.GetFromJsonAsync<List<ReturnView>>(
            $"/api/admin/stores/{world.Store.Store.StoreId}/returns?refund=Failed", CancellationToken);
        var settled = await world.Admin.GetFromJsonAsync<List<ReturnView>>(
            $"/api/admin/stores/{world.Store.Store.StoreId}/returns?refund=Refunded", CancellationToken);

        Assert.Equal(returnId, Assert.Single(owed!).Id);
        Assert.Empty(settled!);
    }

    // The number an alert watches: a customer owed money nobody has confirmed they got back.
    [Fact]
    public async Task A_refund_refused_or_unanswered_is_counted_by_why()
    {
        var refused = await PaidAtComgateAsync(withAccount: false);
        var unanswered = await PaidAtComgateAsync(withAccount: false);
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
            await RefundedOrderAsync(refused);
            _comgate.Says = Transaction(unanswered, "PAID");
            _comgate.RefundGoesUnanswered = true;
            await RefundedOrderAsync(unanswered);
        }

        Assert.Contains("refused", reasons);
        Assert.Contains("unanswered", reasons);
    }

    // Giving money back is the store's decision, the same as refunding an order is.
    [Fact]
    public async Task Somebody_in_support_cannot_try_again_or_close()
    {
        var world = await PaidAtComgateAsync(withAccount: false);
        _comgate.Says = Transaction(world, "CANCELLED");
        var returnId = await RefundedOrderAsync(world);
        var support = await TestUsers.CreateAsync(_factory.Services, world.Store.Store.TenantId, TenantRole.Support);
        using var supportClient = await TestUsers.LoginAsync(_withComgate, support);

        using var triedAgain = await supportClient.PostAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/returns/{returnId}/refund", null, CancellationToken);
        using var closed = await supportClient.PostAsJsonAsync(
            $"/api/admin/stores/{world.Store.Store.StoreId}/returns/{returnId}/refund/close", new { Note = "Mine now." }, CancellationToken);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (triedAgain.StatusCode, closed.StatusCode));
        Assert.Equal(RefundStatus.Failed, (await ReturnRowAsync(world, returnId)).Refund);
    }

    private Task<HttpResponseMessage> AdminAsync(ComgateWorld world, string path, object? body = null) =>
        body is null
            ? world.Admin.PostAsync($"/api/admin/stores/{world.Store.Store.StoreId}/{path}", null, CancellationToken)
            : world.Admin.PostAsJsonAsync($"/api/admin/stores/{world.Store.Store.StoreId}/{path}", body, CancellationToken);

    // The store refunds the whole order, which is a return it makes and receives itself (D-098).
    private async Task<Guid> RefundedOrderAsync(ComgateWorld world)
    {
        using var refunded = await AdminAsync(world, $"orders/{world.Number}/refund");
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);

        return Assert.Single((await refunded.Content.ReadFromJsonAsync<OrderWithReturns>(CancellationToken))!.Returns).Id;
    }

    // The customer sends the chair back and the store agrees to take it; the parcel has not arrived yet.
    private async Task<Guid> AcceptedReturnAsync(ComgateWorld world)
    {
        using var requested = await world.Buyer!.PostAsync(
            $"/api/storefront/account/orders/{world.Number}/returns",
            new { Lines = new[] { new { StoreProductId = world.Store.Products["oak-chair"], Quantity = 1 } }, Reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        var booked = await world.Admin.GetFromJsonAsync<List<ReturnView>>(
            $"/api/admin/stores/{world.Store.Store.StoreId}/returns", CancellationToken);
        var returnId = Assert.Single(booked!).Id;

        using var accepted = await AdminAsync(world, $"returns/{returnId}/accept");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        return returnId;
    }

    private async Task<ComgateWorld> PaidAtComgateAsync(bool withAccount)
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

        var buyer = withAccount ? await BuyerAsync(store) : new StorefrontApi(_withComgate, store.Store);
        using var added = await buyer.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = store.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request(payment: "comgate"));
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var attempt = await _factory.QueryAsync(store.Store, dbContext => dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OrderNumber == order.Number, CancellationToken));
        var world = new ComgateWorld(store, admin, withAccount ? buyer : null, merchant, order.Number, attempt.Reference!, attempt.Amount);

        if (!withAccount)
        {
            buyer.Dispose();
        }

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

    private async Task<StorefrontApi> BuyerAsync(FurnitureStore furniture)
    {
        var email = $"returner-{Guid.NewGuid():N}@example.test";
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

    // A credit note is issued when a return is received, and that needs the invoice the worker writes.
    private Task WaitForInvoiceAsync(FurnitureStore store, string orderNumber) =>
        _factory.EventuallyAsync(
            () => _factory.QueryAsync(store.Store, async dbContext => await dbContext.Set<Invoice>()
                .CountAsync(invoice => invoice.OrderNumber == orderNumber, CancellationToken)),
            count => count > 0,
            CancellationToken);

    private static ComgateTransaction Transaction(ComgateWorld world, string status) =>
        new(world.TransactionId, status, Currency.Of("EUR").ToMinorUnits(world.Total), "EUR", world.Number, Test: true);

    private Task<int> CreditNotesAsync(ComgateWorld world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<Invoice>()
            .CountAsync(invoice => invoice.OrderNumber == world.Number && invoice.Kind == InvoiceKind.CreditNote, CancellationToken));

    private Task<Order> OrderRowAsync(ComgateWorld world) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == world.Number, CancellationToken));

    private Task<OrderReturn> ReturnRowAsync(ComgateWorld world, Guid returnId) =>
        _factory.QueryAsync(world.Store.Store, dbContext => dbContext.Set<OrderReturn>()
            .AsNoTracking()
            .SingleAsync(orderReturn => orderReturn.Id == returnId, CancellationToken));

    private async Task<int> OnHandAsync(ComgateWorld world)
    {
        var stock = await world.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.Single(item => item.VariantId == world.Store.VariantIds["oak-chair"]).OnHand;
    }

    private async Task<int> LettersAsync(ComgateWorld world, string type)
    {
        var payloads = await _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {world.Store.Store.StoreId} AND type = {type}")
            .ToListAsync(CancellationToken));

        return payloads.Count(payload => payload.Contains(world.Number, StringComparison.Ordinal));
    }

    private Task<int> AuditedAsync(ComgateWorld world, string action) =>
        _factory.QueryAsync(world.Store.Store, async dbContext => await dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM auditing.audit_entries WHERE store_id = {world.Store.Store.StoreId} AND action = {action}")
            .SingleAsync(CancellationToken));

    private sealed record ComgateWorld(
        FurnitureStore Store, HttpClient Admin, StorefrontApi? Buyer, string Merchant, string Number, string TransactionId, decimal Total);

    private sealed record ReturnView(Guid Id, string Status, string? Refund, string? RefundProblem, string? RefundNote);

    private sealed record OrderWithReturns(List<ReturnView> Returns);

    private sealed record StockView(Guid VariantId, int OnHand);
}
