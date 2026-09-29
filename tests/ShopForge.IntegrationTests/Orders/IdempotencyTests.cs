using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Invoicing;
using ShopForge.Shared.Http;

namespace ShopForge.IntegrationTests.Orders;

// A shopper whose connection drops has no way to know whether their order was placed, so they press the button
// again. With a key on the request, the second one is answered with what the first one said (D-131).
public sealed class IdempotencyTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_checkout_sent_twice_under_one_key_places_one_order()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await ShopperAsync(furniture, 2);
        var key = NewKey();
        var body = Checkout.Request();

        using var first = await CheckoutAsync(shopper, body, key);
        var placed = await first.Content.ReadAsStringAsync(CancellationToken);
        using var again = await CheckoutAsync(shopper, body, key);
        var replayed = await again.Content.ReadAsStringAsync(CancellationToken);
        var orders = await OrdersAsync(furniture);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(placed, replayed);
        Assert.Equal(first.Headers.Location, again.Headers.Location);
        Assert.False(first.Headers.Contains(Idempotency.ReplayHeaderName));
        Assert.True(again.Headers.Contains(Idempotency.ReplayHeaderName));
        Assert.Single(orders);
    }

    // The same two requests without a key: the second is not the first one's answer, which is what the key buys.
    [Fact]
    public async Task Without_a_key_the_second_attempt_is_a_second_attempt()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await ShopperAsync(furniture, 2);
        var body = Checkout.Request();

        using var first = await CheckoutAsync(shopper, body, key: null);
        using var again = await CheckoutAsync(shopper, body, key: null);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.Created, again.StatusCode);
        Assert.False(again.Headers.Contains(Idempotency.ReplayHeaderName));
    }

    // A refused request did nothing, so its key is not spent: the shopper fixes what was wrong and sends the same
    // request again rather than being read yesterday's refusal.
    [Fact]
    public async Task A_request_that_was_refused_may_be_tried_again_under_the_same_key()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await ShopperAsync(furniture, 2);
        var key = NewKey();

        using var refused = await CheckoutAsync(shopper, Checkout.Request(email: "not-an-address"), key);
        using var fixedUp = await CheckoutAsync(shopper, Checkout.Request(), key);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fixedUp.StatusCode);
        Assert.False(fixedUp.Headers.Contains(Idempotency.ReplayHeaderName));
    }

    // A key belongs to one request. Replaying somebody else's answer because the key was reused by accident would
    // be worse than refusing it.
    [Fact]
    public async Task A_key_used_for_a_different_request_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await ShopperAsync(furniture, 2);
        var key = NewKey();

        using var first = await CheckoutAsync(shopper, Checkout.Request(), key);
        using var different = await CheckoutAsync(shopper, Checkout.Request(email: "someone.else@example.test"), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
    }

    [Fact]
    public async Task Two_identical_checkouts_at_once_place_one_order()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await ShopperAsync(furniture, 2);
        var key = NewKey();
        var body = Checkout.Request();

        var both = await Task.WhenAll(
            Task.Run(() => StatusOfAsync(shopper, body, key), CancellationToken),
            Task.Run(() => StatusOfAsync(shopper, body, key), CancellationToken));
        var orders = await OrdersAsync(furniture);

        // Whichever way they interleave, one of them did the work: the other replayed it or was told it was in
        // flight. What must never happen is two orders.
        Assert.Contains(HttpStatusCode.Created, both);
        Assert.Single(orders);
    }

    [Fact]
    public async Task A_refund_sent_twice_under_one_key_gives_the_money_back_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await ShopperAsync(furniture, 2);
        using var placed = await CheckoutAsync(shopper, Checkout.Request(), key: null);
        var order = await placed.Content.ReadFromJsonAsync<PlacedOrder>(CancellationToken);
        await ConfirmPaymentAsync(furniture, order!.Number);
        var key = NewKey();

        using var first = await RefundAsync(furniture, order.Number, key);
        using var again = await RefundAsync(furniture, order.Number, key);
        var stored = await OrderAsync(furniture, order.Number);
        var creditNotes = await CreditNotesAsync(furniture, order.Number);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.True(again.Headers.Contains(Idempotency.ReplayHeaderName));
        Assert.Equal(stored.GrandTotal, stored.RefundedTotal);
        Assert.Single(creditNotes);
    }

    // The other two mutations a key is honoured on, so that a route losing the filter is caught here rather than
    // by a shopper sending two parcels back.
    [Fact]
    public async Task A_return_asked_for_twice_under_one_key_books_one_return()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer);
        var key = NewKey();
        var body = new { Lines = new[] { new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 } }, Reason = "One is enough." };

        using var first = await buyer.PostAsync($"/api/storefront/account/orders/{order.Number}/returns", body, (Idempotency.HeaderName, key));
        using var again = await buyer.PostAsync($"/api/storefront/account/orders/{order.Number}/returns", body, (Idempotency.HeaderName, key));
        var booked = await ReturnsAsync(furniture, order.Number);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.True(again.Headers.Contains(Idempotency.ReplayHeaderName));
        Assert.Single(booked);
    }

    [Fact]
    public async Task A_parcel_received_twice_under_one_key_gives_the_money_back_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer);
        using var requested = await buyer.PostAsync(
            $"/api/storefront/account/orders/{order.Number}/returns",
            new { Lines = new[] { new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 } }, Reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        var booked = (await ReturnsAsync(furniture, order.Number)).Single();
        await AdminPostAsync(furniture, $"returns/{booked.Id}/accept", key: null);
        var key = NewKey();

        using var first = await AdminPostAsync(furniture, $"returns/{booked.Id}/receive", key);
        using var again = await AdminPostAsync(furniture, $"returns/{booked.Id}/receive", key);
        var stored = await OrderAsync(furniture, order.Number);
        var creditNotes = await CreditNotesAsync(furniture, order.Number);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.True(again.Headers.Contains(Idempotency.ReplayHeaderName));
        Assert.Equal(stored.GrandTotal, stored.RefundedTotal);
        Assert.Single(creditNotes);
    }

    // A key one store used says nothing about another store's request: the rows are the store's own, like the
    // orders they stand for.
    [Fact]
    public async Task A_key_one_store_used_is_free_in_another()
    {
        var one = await FurnitureStore.CreateAsync(factory);
        var other = await FurnitureStore.CreateAsync(factory);
        using var here = await ShopperAsync(one, 1);
        using var there = await ShopperAsync(other, 1);
        var key = NewKey();

        using var first = await CheckoutAsync(here, Checkout.Request(), key);
        using var second = await CheckoutAsync(there, Checkout.Request(), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(second.Headers.Contains(Idempotency.ReplayHeaderName));
    }

    private static string NewKey() => $"key-{Guid.NewGuid():N}";

    private async Task<HttpResponseMessage> AdminPostAsync(FurnitureStore furniture, string path, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/stores/{furniture.Store.StoreId}/{path}");

        if (key is not null)
        {
            request.Headers.Add(Idempotency.HeaderName, key);
        }

        var response = await furniture.Admin.SendAsync(request, CancellationToken);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        return response;
    }

    private async Task<List<ReturnRow>> ReturnsAsync(FurnitureStore furniture, string orderNumber)
    {
        var returns = await furniture.Admin.GetFromJsonAsync<List<ReturnRow>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns", CancellationToken);

        return [.. returns!.Where(candidate => candidate.OrderNumber == orderNumber)];
    }

    private async Task<PlacedOrder> PaidOrderAsync(FurnitureStore furniture, StorefrontApi buyer)
    {
        using var added = await buyer.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        await ConfirmPaymentAsync(furniture, order.Number);

        return order;
    }

    private async Task<StorefrontApi> BuyerAsync(FurnitureStore furniture)
    {
        var email = $"retrier-{Guid.NewGuid():N}@example.test";
        var buyer = new StorefrontApi(factory, furniture.Store);

        await buyer.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Robin", LastName = "Sender", Phone = (string?)null });
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await buyer.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return buyer;
    }

    private async Task<HttpStatusCode> StatusOfAsync(StorefrontApi shopper, object body, string key)
    {
        using var response = await CheckoutAsync(shopper, body, key);

        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> CheckoutAsync(StorefrontApi shopper, object body, string? key) =>
        shopper.PostAsync("/api/storefront/checkout", body, key is null ? null : (Idempotency.HeaderName, key));

    private async Task<HttpResponseMessage> RefundAsync(FurnitureStore furniture, string number, string key)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/stores/{furniture.Store.StoreId}/orders/{number}/refund");
        request.Headers.Add(Idempotency.HeaderName, key);

        return await furniture.Admin.SendAsync(request, CancellationToken);
    }

    private async Task ConfirmPaymentAsync(FurnitureStore furniture, string number)
    {
        using var response = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<StorefrontApi> ShopperAsync(FurnitureStore furniture, int quantity)
    {
        var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["oak-chair"], Quantity = quantity });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        return shopper;
    }

    private Task<List<Order>> OrdersAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Set<Order>().ToListAsync(CancellationToken));

    private Task<Order> OrderAsync(FurnitureStore furniture, string number) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Order>()
            .SingleAsync(order => order.Number == number, CancellationToken));

    private sealed record ReturnRow(Guid Id, string Number, string OrderNumber, string Status);

    private Task<List<Invoice>> CreditNotesAsync(FurnitureStore furniture, string orderNumber) =>
        factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Set<Invoice>()
            .Where(invoice => invoice.OrderNumber == orderNumber && invoice.Kind == InvoiceKind.CreditNote)
            .ToListAsync(CancellationToken));
}
