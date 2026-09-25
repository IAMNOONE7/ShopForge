using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Messaging;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Messaging;

public sealed class OutboxTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_writes_its_event_and_the_worker_sends_the_confirmation()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";

        var order = await PlaceOrderAsync(shopper, email);
        var queued = await MessagesAsync(furniture, order.Number);
        await factory.DispatchOutboxAsync(CancellationToken);
        await factory.DispatchOutboxAsync(CancellationToken);
        var subjects = factory.Emails.For(email).Select(message => message.Subject).ToList();

        // The worker shares this host, so it may already have turned the order's event into an e-mail request;
        // what this asserts is that placing the order wrote its event with it.
        Assert.Contains("order.placed", queued.Select(message => message.Type));
        Assert.Contains(subjects, subject => subject.Contains($"Your order {order.Number}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Paying_and_shipping_tell_the_customer_too()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["walnut-chair"], 1);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        var order = await PlaceOrderAsync(shopper, email);

        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        using var shipped = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/shipment",
            new { TrackingNumber = "PKG-9" },
            CancellationToken);
        await DeliverEverythingAsync();
        var subjects = factory.Emails.For(email).Select(message => message.Subject).ToList();

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (paid.StatusCode, shipped.StatusCode));
        Assert.Contains(subjects, subject => subject.Contains("Payment received", StringComparison.Ordinal));
        Assert.Contains(subjects, subject => subject.Contains("on its way", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_handler_that_keeps_failing_is_retried_and_then_dead_lettered()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["beech-stool"], 1);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        var order = await PlaceOrderAsync(shopper, email);

        await FailingDeliveryAsync(furniture, email);
        var failed = await furniture.Admin.GetFromJsonAsync<List<FailedMessageView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/failed-messages", CancellationToken);
        var dead = failed!.Single(message => message.Type == "email.requested");

        // Five backed-off retries and the attempt that gives up.
        Assert.Equal(6, dead.Attempts);
        Assert.Contains("not answering", dead.Error!, StringComparison.Ordinal);
        Assert.Empty(factory.Emails.For(email));
    }

    [Fact]
    public async Task A_dead_lettered_message_can_be_sent_again()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-bench"], 1);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        var order = await PlaceOrderAsync(shopper, email);
        await FailingDeliveryAsync(furniture, email);

        var failed = await furniture.Admin.GetFromJsonAsync<List<FailedMessageView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/failed-messages", CancellationToken);
        using var requeued = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/failed-messages/{failed!.Single(message => message.Type == "email.requested").Id}/requeue",
            null,
            CancellationToken);
        await factory.DispatchOutboxAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, requeued.StatusCode);
        Assert.NotEmpty(factory.Emails.For(email));
    }

    [Fact]
    public async Task Maintenance_clears_abandoned_carts_and_unconfirmed_sign_ups()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        using var registered = await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Ada", LastName = "Lovelace", Phone = (string?)null });

        await BackdateAsync(furniture);
        await factory.Services.GetRequiredService<StoreMaintenance>().RunAsync(CancellationToken);
        var carts = await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM orders.carts WHERE store_id = {furniture.Store.StoreId}")
            .SingleAsync(CancellationToken));
        var registrations = await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM customers.pending_registrations WHERE store_id = {furniture.Store.StoreId}")
            .SingleAsync(CancellationToken));

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(0, carts);
        Assert.Equal(0, registrations);
    }

    // Runs the outbox until the e-mail gives up. The wait between retries is skipped by pulling the due time back,
    // which is what time passing would do.
    private async Task FailingDeliveryAsync(FurnitureStore furniture, string email)
    {
        factory.EmailDelivery.FailFor(email);

        try
        {
            for (var run = 0; run < 10 && !await HasDeadLetterAsync(furniture); run++)
            {
                await factory.DispatchOutboxAsync(CancellationToken);
                await MakeEverythingDueAsync(furniture);
            }
        }
        finally
        {
            factory.EmailDelivery.StopFailingFor(email);
        }
    }

    private async Task<bool> HasDeadLetterAsync(FurnitureStore furniture) =>
        await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {furniture.Store.StoreId} AND status = 'Failed'")
            .SingleAsync(CancellationToken)) > 0;

    private async Task DeliverEverythingAsync()
    {
        for (var run = 0; run < 3; run++)
        {
            await factory.DispatchOutboxAsync(CancellationToken);
        }
    }

    private Task MakeEverythingDueAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database.ExecuteSqlAsync(
            $"UPDATE messaging.outbox_messages SET due_at = now() WHERE store_id = {furniture.Store.StoreId} AND status = 'Pending'",
            CancellationToken));

    private Task BackdateAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, async dbContext =>
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"UPDATE orders.carts SET updated_at = now() - interval '40 days' WHERE store_id = {furniture.Store.StoreId}",
                CancellationToken);

            return await dbContext.Database.ExecuteSqlAsync(
                $"UPDATE customers.pending_registrations SET created_at = now() - interval '40 days' WHERE store_id = {furniture.Store.StoreId}",
                CancellationToken);
        });

    private Task<List<MessageView>> MessagesAsync(FurnitureStore furniture, string orderNumber) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<MessageView>($"""
                SELECT type AS "Type", status AS "Status"
                FROM messaging.outbox_messages
                WHERE store_id = {furniture.Store.StoreId} AND payload LIKE {'%' + orderNumber + '%'}
                ORDER BY created_at
                """)
            .ToListAsync(CancellationToken));

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrder> PlaceOrderAsync(StorefrontApi shopper, string email)
    {
        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));

        return await shopper.ReadAsync<PlacedOrder>(response, HttpStatusCode.Created);
    }

    private sealed record MessageView(string Type, string Status);

    private sealed record FailedMessageView(Guid Id, string Type, int Attempts, DateTimeOffset CreatedAt, string? Error);
}
