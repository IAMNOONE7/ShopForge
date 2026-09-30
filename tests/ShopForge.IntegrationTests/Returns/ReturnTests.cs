using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Returns;

public sealed class ReturnTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_customer_sends_part_of_an_order_back_and_gets_that_part_of_the_money()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 3)]);
        var chair = furniture.Products["oak-chair"];
        var stockAfterSale = await StockAsync(furniture, furniture.ProductIds["oak-chair"]);

        var requested = await RequestAsync(buyer, order.Number, [(chair, 1)], "One is enough.");
        var accepted = await DecideAsync(furniture, requested.Returns[0].Number, "accept");
        var received = await DecideAsync(furniture, requested.Returns[0].Number, "receive");
        var afterReturn = await buyer.GetJsonAsync<CustomerReturnsView>($"/api/storefront/account/orders/{order.Number}/returns");
        var stored = await OrderAsync(furniture, order.Number);

        Assert.Equal("Accepted", accepted.Status);
        Assert.Equal(("Received", 100m), (received.Status, received.RefundedAmount));
        Assert.Equal(100m, stored.RefundedTotal);
        Assert.Equal(OrderStatus.Paid, stored.Status);
        Assert.Equal(2, afterReturn.Returnable.Single().Quantity);
        Assert.Equal(stockAfterSale.OnHand + 1, (await StockAsync(furniture, furniture.ProductIds["oak-chair"])).OnHand);
    }

    // 3 × 100 plus 4.90 delivery: the delivery comes back only with the last item (D-096).
    [Fact]
    public async Task Sending_everything_back_gives_the_delivery_back_too_and_closes_the_order()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 3)]);
        var chair = furniture.Products["oak-chair"];

        var first = await ReturnAsync(furniture, buyer, order.Number, [(chair, 1)]);
        var second = await ReturnAsync(furniture, buyer, order.Number, [(chair, 2)]);
        var stored = await OrderAsync(furniture, order.Number);
        var creditNotes = await CreditNotesAsync(furniture, order.Number);

        Assert.Equal(100m, first.RefundedAmount);
        Assert.Equal(204.90m, second.RefundedAmount);
        Assert.Equal(304.90m, stored.RefundedTotal);
        Assert.Equal(OrderStatus.Refunded, stored.Status);
        Assert.Equal(stored.GrandTotal, creditNotes.Sum(creditNote => creditNote.Total));
        Assert.Equal(["Oak Chair"], creditNotes[0].Lines.Select(line => line.Description));
        Assert.Equal(["Oak Chair", "Courier"], creditNotes[1].Lines.Select(line => line.Description));
    }

    // 10 % off 3 × 100 is 30, so a chair coming back is worth 100 less its 10 of the discount (D-096).
    [Fact]
    public async Task A_discounted_line_comes_back_with_its_share_of_the_discount()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CreateDiscountAsync(furniture, "TENOFF");
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 3)], discountCode: "TENOFF");
        var chair = furniture.Products["oak-chair"];

        var first = await ReturnAsync(furniture, buyer, order.Number, [(chair, 1)]);
        var rest = await ReturnAsync(furniture, buyer, order.Number, [(chair, 2)]);
        var stored = await OrderAsync(furniture, order.Number);
        var creditNotes = await CreditNotesAsync(furniture, order.Number);

        Assert.Equal(90m, first.RefundedAmount);
        Assert.Equal(10m, creditNotes[0].Lines.Single().Discount);
        Assert.Equal(stored.GrandTotal, first.RefundedAmount + rest.RefundedAmount);
        Assert.Equal(stored.DiscountTotal, creditNotes.SelectMany(creditNote => creditNote.Lines).Sum(line => line.Discount));
    }

    [Fact]
    public async Task A_credit_note_carries_the_vat_of_what_came_back()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 2)]);

        await ReturnAsync(furniture, buyer, order.Number, [(furniture.Products["oak-chair"], 1)]);
        var creditNote = (await CreditNotesAsync(furniture, order.Number)).Single();
        var summary = creditNote.VatSummary().Single();

        Assert.Equal(100m, creditNote.Total);
        Assert.Equal((21m, 82.64m, 17.36m, 100m), (summary.Rate, summary.Net, summary.Vat, summary.Gross));
        Assert.StartsWith("CN-", creditNote.Number, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_customer_who_bought_it_can_send_it_back()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        using var stranger = await BuyerAsync(furniture);
        using var guest = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 1)]);
        var body = new { Lines = new[] { new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 } }, Reason = (string?)null };

        using var theirs = await stranger.PostAsync($"/api/storefront/account/orders/{order.Number}/returns", body);
        using var anonymous = await guest.PostAsync($"/api/storefront/account/orders/{order.Number}/returns", body);

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Nothing_can_be_sent_back_twice()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 2)]);
        var chair = furniture.Products["oak-chair"];

        using var tooMany = await buyer.PostAsync(
            $"/api/storefront/account/orders/{order.Number}/returns",
            new { Lines = new[] { new { StoreProductId = chair, Quantity = 3 } }, Reason = (string?)null });
        await RequestAsync(buyer, order.Number, [(chair, 2)], reason: null);
        using var again = await buyer.PostAsync(
            $"/api/storefront/account/orders/{order.Number}/returns",
            new { Lines = new[] { new { StoreProductId = chair, Quantity = 1 } }, Reason = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_return_asked_for_after_the_window_closed_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SetReturnWindowAsync(furniture, days: 0);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 1)]);

        using var response = await buyer.PostAsync(
            $"/api/storefront/account/orders/{order.Number}/returns",
            new { Lines = new[] { new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 } }, Reason = (string?)null });
        var problem = (await response.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("closed", problem.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refused_return_puts_the_goods_back_on_the_list_and_pays_nothing()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 1)]);
        var requested = await RequestAsync(buyer, order.Number, [(furniture.Products["oak-chair"], 1)], reason: null);

        var refused = await DecideAsync(furniture, requested.Returns[0].Number, "refuse");
        using var received = await ReceiveAsync(furniture, requested.Returns[0].Number);
        var afterRefusal = await buyer.GetJsonAsync<CustomerReturnsView>($"/api/storefront/account/orders/{order.Number}/returns");
        var stored = await OrderAsync(furniture, order.Number);

        Assert.Equal("Refused", refused.Status);
        Assert.Equal(HttpStatusCode.Conflict, received.StatusCode);
        Assert.Equal(1, afterRefusal.Returnable.Single().Quantity);
        Assert.Equal(0m, stored.RefundedTotal);
    }

    [Fact]
    public async Task Receiving_the_same_parcel_twice_pays_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 1)]);
        var requested = await RequestAsync(buyer, order.Number, [(furniture.Products["oak-chair"], 1)], reason: null);
        await DecideAsync(furniture, requested.Returns[0].Number, "accept");

        using var first = await ReceiveAsync(furniture, requested.Returns[0].Number);
        using var second = await ReceiveAsync(furniture, requested.Returns[0].Number);
        var stored = await OrderAsync(furniture, order.Number);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(104.90m, stored.RefundedTotal);
        Assert.Single(await CreditNotesAsync(furniture, order.Number));
    }

    // Two people in the same shop clicking "received" at the same moment must not pay the customer twice.
    [Fact]
    public async Task Two_hands_receiving_at_once_pay_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 2)]);
        var requested = await RequestAsync(buyer, order.Number, [(furniture.Products["oak-chair"], 1)], reason: null);
        await DecideAsync(furniture, requested.Returns[0].Number, "accept");
        var id = (await ReturnsAsync(furniture)).Single(candidate => candidate.Number == requested.Returns[0].Number).Id;

        var answers = await Task.WhenAll(
            furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/returns/{id}/receive", null, CancellationToken),
            furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/returns/{id}/receive", null, CancellationToken));
        var stored = await OrderAsync(furniture, order.Number);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], answers.Select(answer => answer.StatusCode).Order());
        Assert.Equal(100m, stored.RefundedTotal);
        Assert.Single(await CreditNotesAsync(furniture, order.Number));

        foreach (var answer in answers)
        {
            answer.Dispose();
        }
    }

    [Fact]
    public async Task The_store_refunding_an_order_is_a_return_it_makes_itself()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 2)]);

        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        var returns = await ReturnsAsync(furniture);
        var stored = await OrderAsync(furniture, order.Number);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal(("Received", 204.90m, 2), (returns.Single().Status, returns.Single().RefundedAmount, returns.Single().Lines.Single().Quantity));
        Assert.Equal(OrderStatus.Refunded, stored.Status);
    }

    [Fact]
    public async Task What_a_customer_already_sent_back_is_not_refunded_again_by_the_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 3)]);
        await ReturnAsync(furniture, buyer, order.Number, [(furniture.Products["oak-chair"], 1)]);

        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        using var again = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        var stored = await OrderAsync(furniture, order.Number);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(stored.GrandTotal, stored.RefundedTotal);
        Assert.Equal(OrderStatus.Refunded, stored.Status);
    }

    [Fact]
    public async Task One_store_never_sees_another_store_s_returns()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var other = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 1)]);
        var requested = await RequestAsync(buyer, order.Number, [(furniture.Products["oak-chair"], 1)], reason: null);
        var id = (await ReturnsAsync(furniture)).Single().Id;

        var theirs = await other.Admin.GetFromJsonAsync<List<AdminReturnView>>(
            $"/api/admin/stores/{other.Store.StoreId}/returns", CancellationToken);
        using var reachedAcross = await other.Admin.PostAsync(
            $"/api/admin/stores/{other.Store.StoreId}/returns/{id}/accept", null, CancellationToken);

        Assert.Equal(requested.Returns[0].Number, (await ReturnsAsync(furniture)).Single().Number);
        Assert.Empty(theirs!);
        Assert.Equal(HttpStatusCode.NotFound, reachedAcross.StatusCode);
    }

    [Fact]
    public async Task The_customer_is_told_what_happened_to_their_return()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var buyer = await BuyerAsync(furniture);
        var order = await PaidOrderAsync(furniture, buyer, [("oak-chair", 1)]);
        var email = (await OrderAsync(furniture, order.Number)).Email;

        var refunded = await ReturnAsync(furniture, buyer, order.Number, [(furniture.Products["oak-chair"], 1)]);
        var letters = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.For(email)),
            messages => messages.Any(message => message.Body.Contains(refunded.Number, StringComparison.Ordinal)
                && message.Body.Contains("on its way back", StringComparison.Ordinal)),
            CancellationToken);

        Assert.Contains(letters, message => message.Subject.Contains("Refund for order", StringComparison.Ordinal));
    }

    private async Task<CustomerReturnView> ReturnAsync(
        FurnitureStore furniture,
        StorefrontApi buyer,
        string orderNumber,
        (Guid Product, int Quantity)[] lines)
    {
        var requested = await RequestAsync(buyer, orderNumber, lines, reason: null);
        var number = requested.Returns.OrderByDescending(candidate => candidate.RequestedAt).First().Number;

        await DecideAsync(furniture, number, "accept");

        return await DecideAsync(furniture, number, "receive");
    }

    private async Task<CustomerReturnsView> RequestAsync(
        StorefrontApi buyer,
        string orderNumber,
        (Guid Product, int Quantity)[] lines,
        string? reason)
    {
        using var response = await buyer.PostAsync(
            $"/api/storefront/account/orders/{orderNumber}/returns",
            new { Lines = lines.Select(line => new { StoreProductId = line.Product, line.Quantity }), Reason = reason });

        return await buyer.ReadAsync<CustomerReturnsView>(response);
    }

    private async Task<CustomerReturnView> DecideAsync(FurnitureStore furniture, string returnNumber, string decision)
    {
        var id = (await ReturnsAsync(furniture)).Single(candidate => candidate.Number == returnNumber).Id;
        using var response = decision == "receive"
            ? await ReceiveAsync(furniture, returnNumber)
            : await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/returns/{id}/{decision}", null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decided = (await response.Content.ReadFromJsonAsync<AdminReturnView>(CancellationToken))!;

        return new CustomerReturnView(decided.Number, decided.Status, decided.RequestedAt, decided.RefundedAmount);
    }

    private async Task<HttpResponseMessage> ReceiveAsync(FurnitureStore furniture, string returnNumber)
    {
        var id = (await ReturnsAsync(furniture)).Single(candidate => candidate.Number == returnNumber).Id;

        return await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/returns/{id}/receive", null, CancellationToken);
    }

    private async Task<List<AdminReturnView>> ReturnsAsync(FurnitureStore furniture) =>
        (await furniture.Admin.GetFromJsonAsync<List<AdminReturnView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns", CancellationToken))!;

    private async Task<StorefrontApi> BuyerAsync(FurnitureStore furniture)
    {
        var email = $"returner-{Guid.NewGuid():N}@example.test";
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

    private async Task<PlacedOrder> PaidOrderAsync(
        FurnitureStore furniture,
        StorefrontApi buyer,
        (string Product, int Quantity)[] lines,
        string? discountCode = null)
    {
        foreach (var (product, quantity) in lines)
        {
            using var added = await buyer.PostAsync(
                "/api/storefront/cart/items",
                new { StoreProductId = furniture.Products[product], Quantity = quantity });
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }

        if (discountCode is not null)
        {
            using var applied = await buyer.PutAsync("/api/storefront/cart/discount", new { Code = discountCode });
            Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        }

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        // The invoice is issued by the worker; a return waits for nothing, but the documents assertions do.
        await factory.EventuallyAsync(
            () => factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Set<Invoice>()
                .CountAsync(invoice => invoice.OrderNumber == order.Number, CancellationToken)),
            count => count > 0,
            CancellationToken);

        return order;
    }

    private Task<Order> OrderAsync(FurnitureStore furniture, string number) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Order>()
            .SingleAsync(order => order.Number == number, CancellationToken));

    private Task<List<Invoice>> CreditNotesAsync(FurnitureStore furniture, string orderNumber) =>
        factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Set<Invoice>()
            .Where(invoice => invoice.OrderNumber == orderNumber && invoice.Kind == InvoiceKind.CreditNote)
            .OrderBy(invoice => invoice.Number)
            .ToListAsync(CancellationToken));

    private async Task<StockView> StockAsync(FurnitureStore furniture, Guid productId)
    {
        var stock = await furniture.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);
        var variantId = await furniture.Admin.DefaultVariantIdAsync(productId);

        return stock!.Single(item => item.VariantId == variantId);
    }

    private async Task CreateDiscountAsync(FurnitureStore furniture, string code)
    {
        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/discounts",
            new { Code = code, Name = "Ten off", Kind = "Percentage", Value = 10m },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task SetReturnWindowAsync(FurnitureStore furniture, int days)
    {
        var stores = await furniture.Admin.GetFromJsonAsync<List<AdminStoreView>>("/api/admin/stores", CancellationToken);
        var store = stores!.Single(candidate => candidate.Id == furniture.Store.StoreId);

        using var response = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}",
            new
            {
                store.Name,
                store.Currency,
                store.Culture,
                Theme = new { store.Theme.PrimaryColor, store.Theme.SecondaryColor, store.Theme.BorderRadius },
                ReturnWindowDays = days,
                Company = store.Company,
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record CustomerReturnsView(DateTimeOffset? ClosesAt, List<ReturnableView> Returnable, List<CustomerReturnView> Returns);

    private sealed record ReturnableView(Guid StoreProductId, string ProductName, int Quantity);

    private sealed record CustomerReturnView(string Number, string Status, DateTimeOffset RequestedAt, decimal RefundedAmount);

    private sealed record AdminReturnView(
        Guid Id,
        string Number,
        string OrderNumber,
        string Status,
        DateTimeOffset RequestedAt,
        decimal RefundedAmount,
        string? Reason,
        List<AdminReturnLineView> Lines);

    private sealed record AdminReturnLineView(string ProductName, int Quantity);

    private sealed record AdminStoreView(Guid Id, string Name, string Currency, string Culture, ThemeView Theme, int ReturnWindowDays, object? Company);

    private sealed record ThemeView(string PrimaryColor, string SecondaryColor, int BorderRadius);

    private sealed record StockView(Guid VariantId, int OnHand, int Reserved, int Available);

    private sealed record ProblemView(string Title);
}
