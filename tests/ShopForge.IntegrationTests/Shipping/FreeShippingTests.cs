using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Shipping;

// Delivery costs nothing once the goods come to the threshold the merchant switched on (D-221): by every method, kept
// on the order as a free-shipping code is, and followed by the invoice and a refund. The furniture store's chairs are
// 100 each and its courier 4.90.
public sealed class FreeShippingTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Free_delivery_is_off_until_the_merchant_switches_it_on()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shopper = await CartAsync(furniture, chairs: 4);

        var store = await StoreAsync(furniture);
        var methods = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        var courier = methods.ShippingMethods.Single(method => method.Code == "courier");

        Assert.Equal(new FreeShippingSettingView(false, null), store.FreeShipping);
        Assert.Equal((4.90m, 4.90m), (courier.Price, courier.FullPrice));
        Assert.Null(methods.FreeShipping);
        Assert.Null((await shopper.GetJsonAsync<CartView>("/api/storefront/cart")).FreeShipping);
        Assert.Null((await shopper.GetJsonAsync<StoreReadView>("/api/storefront/store")).FreeShippingFrom);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, 0.0)]
    [InlineData(false, -1.0)]
    [InlineData(true, 99.999)]
    public async Task A_threshold_is_more_than_nothing_and_in_the_stores_currency(bool isOn, double? threshold)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await SetFreeShippingAsync(furniture, isOn, (decimal?)threshold);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("freeShipping.threshold", (await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!.Errors.Keys);
        Assert.Equal(new FreeShippingSettingView(false, null), (await StoreAsync(furniture)).FreeShipping);
    }

    // A commercial term every quote follows, so a change is kept with what it was. Switched off, the threshold stays.
    [Fact]
    public async Task Switching_it_is_audited_and_switching_it_off_keeps_the_threshold()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var on = await SetFreeShippingAsync(furniture, true, 250m);
        using var again = await SetFreeShippingAsync(furniture, true, 250m);
        using var withoutIt = await SetFreeShippingAsync(furniture, freeShipping: null);
        using var off = await SetFreeShippingAsync(furniture, false, 250m);

        var trail = await furniture.Admin.GetFromJsonAsync<List<AuditView>>("/api/admin/audit?action=store.free-shipping-changed", CancellationToken);
        Assert.All(new[] { on, again, withoutIt, off }, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, trail!.Count);
        Assert.Contains(trail, entry => entry.Details!.Contains("\"before\":{\"isOn\":false,\"threshold\":null}", StringComparison.Ordinal));
        Assert.Equal(new FreeShippingSettingView(false, 250m), (await StoreAsync(furniture)).FreeShipping);
        var shopper = await CartAsync(furniture, chairs: 4);
        Assert.Equal(4.90m, (await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods")).ShippingMethods.Single(method => method.Code == "courier").Price);
        Assert.Null((await shopper.GetJsonAsync<StoreReadView>("/api/storefront/store")).FreeShippingFrom);
    }

    // Exactly the threshold reaches it, by a flat method and one priced by the parcel alike; a cent below, nothing changes.
    [Fact]
    public async Task At_the_threshold_delivery_is_free_by_every_method_and_a_cent_below_it_is_not()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var van = await OwnVan.CreateAsync(factory, furniture);
        var shopper = await CartAsync(furniture, chairs: 2);

        await SwitchOnAsync(furniture, 200m);
        var free = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods?country=CZ");
        await SwitchOnAsync(furniture, 200.01m);
        var paid = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods?country=CZ");

        // Both free, so neither is cheaper: compared by code, not by the listing's order.
        Assert.Equal([("courier", 0m, 4.90m), (van, 0m, 8m)], free.ShippingMethods.OrderBy(method => method.Code, StringComparer.Ordinal).Select(method => (method.Code, method.Price, method.FullPrice)));
        Assert.Equal(new FreeShippingView(200m, 0m), free.FreeShipping);
        Assert.Equal([("courier", 4.90m, 4.90m), (van, 8m, 8m)], paid.ShippingMethods.Select(method => (method.Code, method.Price, method.FullPrice)));
        Assert.Equal(new FreeShippingView(200.01m, 0.01m), paid.FreeShipping);
    }

    // The order keeps the price and a discount of all of it, marked as the threshold's and not a code's; the invoice
    // prints the delivery with that discount, which carries no VAT, and a full refund gives nothing back for it.
    [Fact]
    public async Task The_order_keeps_the_price_takes_it_off_and_the_invoice_and_a_refund_agree()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SwitchOnAsync(furniture, 200m);
        var shopper = await CartAsync(furniture, chairs: 2);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", CourierCheckout(expectedShippingPrice: 0m));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var confirmation = await shopper.GetJsonAsync<ConfirmationView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var stored = await factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Order>().SingleAsync(candidate => candidate.Number == order.Number, CancellationToken));
        using var paidFor = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        var invoice = await factory.EventuallyAsync(
            () => factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Invoice>()
                .SingleOrDefaultAsync(candidate => candidate.OrderNumber == order.Number && candidate.Kind == InvoiceKind.Invoice, CancellationToken)),
            found => found is not null,
            CancellationToken);
        using var refunded = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        var afterwards = await refunded.Content.ReadFromJsonAsync<AdminOrderView>(CancellationToken);

        Assert.Equal((0m, 200m, (decimal?)200m), (confirmation.ShippingPrice, confirmation.GrandTotal, confirmation.FreeShippingThreshold));
        Assert.Null(confirmation.Discount);
        Assert.Equal((4.90m, 4.90m, 0m, (decimal?)200m), (stored.ShippingPrice, stored.ShippingDiscount, stored.DiscountTotal, stored.FreeShippingThreshold));
        var delivery = Assert.Single(invoice!.Lines, line => line.Description == "Courier");
        Assert.Equal((4.90m, 4.90m, 0m), (delivery.UnitPrice, delivery.Discount, delivery.LineTotal));
        Assert.Equal((200m, stored.VatTotal), (invoice.Total, invoice.VatTotal));
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal((200m, (decimal?)200m), (Assert.Single(afterwards!.Returns).RefundedAmount, afterwards.FreeShippingThreshold));
    }

    // The goods are counted as the shopper pays for them: after the code (the owner's choice).
    [Fact]
    public async Task The_goods_are_counted_after_a_code()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CodeAsync(furniture, "TENOFF", "Percentage", 10m);
        await SwitchOnAsync(furniture, 200m);
        var short_ = await CartAsync(furniture, chairs: 2, code: "TENOFF");
        var enough = await CartAsync(furniture, chairs: 3, code: "TENOFF");

        var shortMethods = await short_.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        var enoughMethods = await enough.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        using var placed = await short_.PostAsync("/api/storefront/checkout", CourierCheckout(expectedShippingPrice: 4.90m));
        var order = await short_.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Order>().SingleAsync(candidate => candidate.Number == order.Number, CancellationToken));

        Assert.Equal((4.90m, new FreeShippingView(200m, 20m)), (shortMethods.ShippingMethods.Single().Price, shortMethods.FreeShipping));
        Assert.Equal((0m, new FreeShippingView(200m, 0m)), (enoughMethods.ShippingMethods.Single().Price, enoughMethods.FreeShipping));
        Assert.Equal((0m, 20m, (decimal?)null, 184.90m), (stored.ShippingDiscount, stored.DiscountTotal, stored.FreeShippingThreshold, stored.GrandTotal));
    }

    // A code that only takes delivery off would add nothing, and a code that can be used once must not be spent on that.
    [Fact]
    public async Task A_free_shipping_code_is_refused_while_delivery_is_already_free()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await CodeAsync(furniture, "SHIPFREE", "FreeShipping", 0m);
        await SwitchOnAsync(furniture, 200m);
        var reached = await CartAsync(furniture, chairs: 2);
        var growing = await CartAsync(furniture, chairs: 1, code: "SHIPFREE");

        using var refused = await reached.PutAsync("/api/storefront/cart/discount", new { Code = "SHIPFREE" });
        await AddChairsAsync(furniture, growing, 1);
        var cart = await growing.GetJsonAsync<CartView>("/api/storefront/cart");
        using var stopped = await growing.PostAsync("/api/storefront/checkout", CourierCheckout(expectedShippingPrice: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("already free", (await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!.Errors["code"][0], StringComparison.Ordinal);
        Assert.Contains("already free", cart.DiscountProblem, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, stopped.StatusCode);
        Assert.Equal(0, await factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Discount>()
            .Where(discount => discount.Code == "SHIPFREE").Select(discount => discount.Redemptions).SingleAsync(CancellationToken)));
    }

    // The shopper was shown what they pay, so that is the price confirmed: the full price is the moved one.
    [Fact]
    public async Task The_price_confirmed_is_what_the_shopper_pays()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var van = await OwnVan.CreateAsync(factory, furniture);
        await SwitchOnAsync(furniture, 200m);
        var shopper = await CartAsync(furniture, chairs: 2);

        using var moved = await shopper.PostAsync("/api/storefront/checkout", CourierCheckout(expectedShippingPrice: 4.90m));
        var problem = await moved.Content.ReadFromJsonAsync<PriceProblemView>(CancellationToken);
        using var placed = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(van, expectedShippingPrice: 0m));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await furniture.Admin.GetFromJsonAsync<AdminOrderView>($"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}", CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal(new QuoteView(4.90m, 4.90m, 0m, 200m), problem!.ShippingQuote);
        Assert.Contains("costs 0.00 EUR", problem.Detail, StringComparison.Ordinal);
        Assert.Equal((0m, (decimal?)200m), (stored!.ShippingPrice, stored.FreeShippingThreshold));
        Assert.Single(stored.Parcels);
    }

    [Fact]
    public async Task The_cart_and_the_store_say_how_much_more_makes_delivery_free()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SwitchOnAsync(furniture, 250m);
        var shopper = await CartAsync(furniture, chairs: 2);

        using var newcomer = new StorefrontApi(factory, furniture.Store);

        Assert.Equal(new FreeShippingView(250m, 50m), (await shopper.GetJsonAsync<CartView>("/api/storefront/cart")).FreeShipping);
        Assert.Equal(250m, (await shopper.GetJsonAsync<StoreReadView>("/api/storefront/store")).FreeShippingFrom);
        Assert.Equal(new FreeShippingView(250m, 250m), (await newcomer.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods")).FreeShipping);
    }

    // The preview is the quote the checkout asks, at the listings' prices.
    [Fact]
    public async Task The_merchants_preview_shows_free_delivery_at_the_threshold()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var van = await OwnVan.CreateAsync(factory, furniture);
        await SwitchOnAsync(furniture, 200m);

        var two = await PreviewAsync(furniture, van, chairs: 2);
        var one = await PreviewAsync(furniture, van, chairs: 1);

        Assert.Equal(new QuoteView(8m, 8m, 0m, 200m), two);
        Assert.Equal(new QuoteView(8m, 0m, 8m, null), one);
    }

    // The threshold comes with the settings the cart already reads, so the listing asks nothing more for it.
    [Fact]
    public async Task Free_delivery_costs_the_methods_listing_no_more_queries()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await OwnVan.CreateAsync(factory, furniture);
        var shopper = await CartAsync(furniture, chairs: 2);

        var off = await CountedMethodsAsync(shopper);
        await SwitchOnAsync(furniture, 200m);
        var on = await CountedMethodsAsync(shopper);

        Assert.Equal(off, on);
    }

    private async Task<StorefrontApi> CartAsync(FurnitureStore furniture, int chairs, string? code = null)
    {
        var shopper = new StorefrontApi(factory, furniture.Store);
        await AddChairsAsync(furniture, shopper, chairs);

        if (code is not null)
        {
            using var applied = await shopper.PutAsync("/api/storefront/cart/discount", new { Code = code });
            Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        }

        return shopper;
    }

    private static async Task AddChairsAsync(FurnitureStore furniture, StorefrontApi shopper, int chairs)
    {
        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = chairs });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
    }

    private async Task SwitchOnAsync(FurnitureStore furniture, decimal threshold)
    {
        using var response = await SetFreeShippingAsync(furniture, true, threshold);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> SetFreeShippingAsync(FurnitureStore furniture, bool isOn, decimal? threshold) =>
        SetFreeShippingAsync(furniture, new { IsOn = isOn, Threshold = threshold });

    // The store's settings as they are, with free delivery as given; nothing given leaves it alone.
    private async Task<HttpResponseMessage> SetFreeShippingAsync(FurnitureStore furniture, object? freeShipping)
    {
        var store = await StoreAsync(furniture);

        return await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}",
            new
            {
                store.Name,
                store.Currency,
                store.Culture,
                Theme = new { store.Theme.PrimaryColor, store.Theme.SecondaryColor, store.Theme.BorderRadius },
                store.ReturnWindowDays,
                store.Company,
                FreeShipping = freeShipping,
            },
            CancellationToken);
    }

    private async Task<AdminStoreView> StoreAsync(FurnitureStore furniture) =>
        (await furniture.Admin.GetFromJsonAsync<List<AdminStoreView>>("/api/admin/stores", CancellationToken))!
            .Single(candidate => candidate.Id == furniture.Store.StoreId);

    private static async Task CodeAsync(FurnitureStore furniture, string code, string kind, decimal value)
    {
        using var created = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/discounts",
            new { Code = code, Name = $"{code} discount", Kind = kind, Value = value, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private static async Task<QuoteView> PreviewAsync(FurnitureStore furniture, string code, int chairs)
    {
        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/quote",
            new { Country = "CZ", Lines = new[] { new { StoreProductId = furniture.Products["oak-chair"], Quantity = chairs } } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<QuoteView>(CancellationToken))!;
    }

    private async Task<int> CountedMethodsAsync(StorefrontApi shopper)
    {
        var tally = QueryCounter.NewTally();
        using var response = await shopper.GetAsync("/api/storefront/checkout/methods?country=CZ", (QueryCounter.HeaderName, tally));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return factory.Queries[tally];
    }

    // The courier's checkout, with the delivery price the shopper was shown.
    private static object CourierCheckout(decimal expectedShippingPrice) => new
    {
        Email = "buyer@example.test",
        Phone = "+420 123 456 789",
        BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Dublin", PostalCode = "D01 AB12", Country = "IE" },
        ShippingAddress = (object?)null,
        PaymentMethodCode = "bank-transfer",
        ShippingMethodCode = "courier",
        PickupPointCode = (string?)null,
        ExpectedShippingPrice = expectedShippingPrice,
    };

    private sealed record AdminStoreView(Guid Id, string Name, string Currency, string Culture, ThemeView Theme, int ReturnWindowDays, object? Company, FreeShippingSettingView FreeShipping);

    private sealed record ThemeView(string PrimaryColor, string SecondaryColor, int BorderRadius);

    private sealed record FreeShippingSettingView(bool IsOn, decimal? Threshold);

    private sealed record FreeShippingView(decimal Threshold, decimal Remaining);

    private sealed record MethodsView(List<MethodView> ShippingMethods, FreeShippingView? FreeShipping);

    private sealed record MethodView(string Code, decimal Price, decimal FullPrice);

    private sealed record CartView(FreeShippingView? FreeShipping, string? DiscountProblem);

    private sealed record StoreReadView(decimal? FreeShippingFrom);

    private sealed record ConfirmationView(decimal ShippingPrice, decimal GrandTotal, DiscountView? Discount, decimal? FreeShippingThreshold);

    private sealed record DiscountView(string Code, decimal Amount);

    private sealed record AdminOrderView(decimal ShippingPrice, decimal? FreeShippingThreshold, List<AdminReturnView> Returns, List<object> Parcels);

    private sealed record AdminReturnView(decimal RefundedAmount);

    private sealed record QuoteView(decimal Total, decimal ShippingDiscount, decimal Charged, decimal? FreeShippingThreshold);

    private sealed record PriceProblemView(string Detail, QuoteView ShippingQuote);

    private sealed record AuditView(string Action, string Subject, string? Details);

    private sealed record ProblemView(Dictionary<string, string[]> Errors);
}
