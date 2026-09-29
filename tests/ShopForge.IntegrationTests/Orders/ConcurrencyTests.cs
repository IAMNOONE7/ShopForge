using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Orders;

// Two people, or a person and a machine, reaching for one order at the same moment. What matters is not who wins
// but that only one of them does, and that the loser is told rather than quietly overwriting the winner (D-130).
public sealed class ConcurrencyTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The shape of the bug this guards: both writers read an order that is awaiting payment, so both believe the
    // transition is theirs to make. Before the row carried a version, both updates went through.
    [Fact]
    public async Task The_second_writer_of_one_order_changes_nothing_and_is_told_so()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var order = await PlacedOrderAsync(furniture);

        await using var first = TestStores.CreateScope(factory.Services, furniture.Store);
        await using var second = TestStores.CreateScope(factory.Services, furniture.Store);
        var one = first.ServiceProvider.GetRequiredService<DbContext>();
        var other = second.ServiceProvider.GetRequiredService<DbContext>();
        var paying = await OrderAsync(one, order.Number);
        var cancelling = await OrderAsync(other, order.Number);

        Assert.True(paying.ConfirmPayment(DateTimeOffset.UtcNow));
        Assert.True(cancelling.Cancel());
        await one.SaveChangesAsync(CancellationToken);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync(CancellationToken));
        Assert.Equal(OrderStatus.Paid, (await OrderAsync(furniture, order.Number)).Status);
    }

    // The same thing through the front door, where what the loser gets is a 409 rather than a fault — and, the
    // point of the whole stage, one letter to the customer and one movement of stock.
    [Fact]
    public async Task Two_admins_marking_one_order_paid_leave_one_payment_and_one_letter()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var order = await PlacedOrderAsync(furniture);
        var stockBefore = await StockAsync(furniture);

        var attempts = await Task.WhenAll(
            Task.Run(() => ConfirmPaymentAsync(furniture, order.Number), CancellationToken),
            Task.Run(() => ConfirmPaymentAsync(furniture, order.Number), CancellationToken));
        var letters = await LettersAsync(furniture, "order.paid", order.Number);
        var stockAfter = await StockAsync(furniture);

        Assert.Equal(1, attempts.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, attempts.Count(status => status == HttpStatusCode.Conflict));
        Assert.Equal(1, letters);
        Assert.Equal((stockBefore.OnHand - 2, 0), (stockAfter.OnHand, stockAfter.Reserved));
    }

    // Two parcels of one order arriving together. Each works out what to refund from what the other has not
    // committed yet, so before the order's row was taken first, neither saw itself as the last one back and the
    // delivery was refunded with neither of them (D-096).
    [Fact]
    public async Task Two_parcels_of_one_order_arriving_together_still_give_the_delivery_back()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, 2);
        var first = await AcceptedReturnAsync(furniture, buyer, order.Number, 1);
        var second = await AcceptedReturnAsync(furniture, buyer, order.Number, 1);

        var received = await WhileHoldingTheOrderAsync(
            furniture,
            order.Number,
            () => Task.WhenAll(
                Task.Run(() => ReceiveAsync(furniture, first), CancellationToken),
                Task.Run(() => ReceiveAsync(furniture, second), CancellationToken)));

        var stored = await OrderAsync(furniture, order.Number);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], received);
        Assert.Equal(stored.GrandTotal, stored.RefundedTotal);
        Assert.Equal(OrderStatus.Refunded, stored.Status);
    }

    // Two requests for the last item of an order: one of them has nothing left to ask for.
    [Fact]
    public async Task Two_requests_for_the_last_item_cannot_both_book_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, 1);

        var asked = await WhileHoldingTheOrderAsync(
            furniture,
            order.Number,
            () => Task.WhenAll(
                Task.Run(() => RequestReturnAsync(buyer, order.Number, 1), CancellationToken),
                Task.Run(() => RequestReturnAsync(buyer, order.Number, 1), CancellationToken)));

        var booked = await ReturnsAsync(furniture, order.Number);

        Assert.Equal(1, asked.Count(status => status == HttpStatusCode.OK));
        Assert.Single(booked);
    }

    // Holding the order's row makes the requests behind it queue instead of racing, so what the test proves is the
    // ordering the guard imposes rather than whichever interleaving the machine happened to produce that run.
    private async Task<HttpStatusCode[]> WhileHoldingTheOrderAsync(
        FurnitureStore furniture,
        string orderNumber,
        Func<Task<HttpStatusCode[]>> requests)
    {
        var orderId = (await OrderAsync(furniture, orderNumber)).Id;

        await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SELECT 1 FROM orders.orders WHERE id = {0} FOR UPDATE", [orderId], CancellationToken);

        var running = requests();
        await Task.Delay(500, CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        return await running;
    }

    private async Task<HttpStatusCode> ConfirmPaymentAsync(FurnitureStore furniture, string number)
    {
        using var response = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{number}/payment", null, CancellationToken);

        return response.StatusCode;
    }

    private async Task<HttpStatusCode> ReceiveAsync(FurnitureStore furniture, Guid returnId)
    {
        using var response = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns/{returnId}/receive", null, CancellationToken);

        return response.StatusCode;
    }

    private async Task<HttpStatusCode> RequestReturnAsync(StorefrontApi buyer, string orderNumber, int quantity)
    {
        using var response = await buyer.PostAsync(
            $"/api/storefront/account/orders/{orderNumber}/returns",
            new { Lines = new[] { new { StoreProductId = _chair, Quantity = quantity } }, Reason = (string?)null });

        return response.StatusCode;
    }

    private async Task<Guid> AcceptedReturnAsync(FurnitureStore furniture, StorefrontApi buyer, string orderNumber, int quantity)
    {
        Assert.Equal(HttpStatusCode.OK, await RequestReturnAsync(buyer, orderNumber, quantity));

        var latest = (await ReturnsAsync(furniture, orderNumber)).OrderByDescending(candidate => candidate.RequestedAt).First();
        using var accepted = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns/{latest.Id}/accept", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        return latest.Id;
    }

    private async Task<List<ReturnRow>> ReturnsAsync(FurnitureStore furniture, string orderNumber)
    {
        var returns = await furniture.Admin.GetFromJsonAsync<List<ReturnRow>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns", CancellationToken);

        return [.. returns!.Where(candidate => candidate.OrderNumber == orderNumber)];
    }

    private async Task<PlacedOrder> PlacedOrderAsync(FurnitureStore furniture)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        _chair = furniture.Products["oak-chair"];

        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = _chair, Quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
    }

    private async Task<PlacedOrder> PaidOrderAsync(FurnitureStore furniture, StorefrontApi buyer, int quantity)
    {
        _chair = furniture.Products["oak-chair"];

        using var added = await buyer.PostAsync("/api/storefront/cart/items", new { StoreProductId = _chair, Quantity = quantity });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.OK, await ConfirmPaymentAsync(furniture, order.Number));

        return order;
    }

    private async Task<StorefrontApi> BuyerAsync(FurnitureStore furniture)
    {
        var email = $"racer-{Guid.NewGuid():N}@example.test";
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

    private async Task<int> LettersAsync(FurnitureStore furniture, string type, string orderNumber)
    {
        await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var payloads = await dbContext.Database
            .SqlQuery<string>($"SELECT payload AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {furniture.Store.StoreId} AND type = {type}")
            .ToListAsync(CancellationToken);

        return payloads.Count(payload => payload.Contains(orderNumber, StringComparison.Ordinal));
    }

    private async Task<StockRow> StockAsync(FurnitureStore furniture)
    {
        var stock = await furniture.Admin.GetFromJsonAsync<List<StockRow>>("/api/admin/stock", CancellationToken);

        return stock!.Single(item => item.ProductId == furniture.ProductIds["oak-chair"]);
    }

    private Task<Order> OrderAsync(FurnitureStore furniture, string number) =>
        factory.QueryAsync(furniture.Store, dbContext => OrderAsync(dbContext, number));

    private static Task<Order> OrderAsync(DbContext dbContext, string number) =>
        dbContext.Set<Order>().SingleAsync(order => order.Number == number, CancellationToken);

    private Guid _chair;

    private sealed record ReturnRow(Guid Id, string Number, string OrderNumber, string Status, DateTimeOffset RequestedAt);

    private sealed record StockRow(Guid ProductId, int OnHand, int Reserved, int Available);
}
