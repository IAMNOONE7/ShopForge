using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Shipping;

// The shopper sees, chooses and pays the quoted price, the order keeps the parcels it was priced on, and nothing that
// publishes a delivery price contradicts it (D-220). Ten chairs by the store's own van make two parcels, 20 in all.
public sealed class ParcelCheckoutTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Each method answers with its quote and how many parcels it estimates; a flat method answers as before.
    [Fact]
    public async Task A_method_priced_by_the_parcel_is_offered_at_its_quote()
    {
        var (furniture, code, shopper) = await CartAsync(chairs: 10);

        var methods = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods?country=CZ");
        var van = methods.ShippingMethods.Single(method => method.Code == code);
        var courier = methods.ShippingMethods.Single(method => method.Code == "courier");

        Assert.Equal((20m, "perParcel", (int?)2), (van.Price, van.Pricing, van.Parcels));
        Assert.Equal(new MapParcelView(7.1m, 45, 40, 9), van.MapParcel);
        Assert.Equal((4.90m, "flat", (int?)null, (MapParcelView?)null), (courier.Price, courier.Pricing, courier.Parcels, courier.MapParcel));
        Assert.Equal(["courier", code], methods.ShippingMethods.Select(method => method.Code));
        Assert.Empty(methods.UnavailableShippingMethods);
    }

    // A method that cannot take the cart is listed apart, with a reason a shopper can act on.
    [Theory]
    [InlineData("notToCountry")]
    [InlineData("tooManyParcels")]
    [InlineData("tooLarge")]
    [InlineData("missingData")]
    public async Task A_method_that_cannot_take_the_cart_is_listed_apart_with_its_reason(string reason)
    {
        var (furniture, code, shopper) = await CartAsync(chairs: 10);
        Guid? toBlame = null;

        switch (reason)
        {
            case "tooManyParcels":
                using (var settled = await furniture.Admin.PutAsJsonAsync(
                    $"/api/admin/stores/{furniture.Store.StoreId}/shipping-settings", new { PackagingWeightGrams = 300, FillPercent = 70, LargestParcelOrder = 1 }, CancellationToken))
                {
                    Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
                }

                break;
            case "tooLarge":
                toBlame = await ListedAsync(furniture, shopper, weight: 30_000, 2_000, 700, 700);
                break;
            case "missingData":
                toBlame = await ListedAsync(furniture, shopper, weight: 900, 300, 200, 100, unsize: true);
                break;
        }

        var methods = await shopper.GetJsonAsync<MethodsView>($"/api/storefront/checkout/methods?country={(reason == "notToCountry" ? "SK" : "CZ")}");

        Assert.DoesNotContain(code, methods.ShippingMethods.Select(method => method.Code));
        var unavailable = Assert.Single(methods.UnavailableShippingMethods, method => method.Code == code);
        Assert.Equal((OwnVan.MethodName, reason), (unavailable.Name, unavailable.Reason));
        Assert.Equal(toBlame, unavailable.VariantId);
    }

    // The order's delivery is the quote the shopper was shown, and it keeps the parcels the merchant's preview gives for
    // the same cart.
    [Fact]
    public async Task The_order_is_priced_by_the_quote_and_keeps_the_previews_parcels()
    {
        var (furniture, code, shopper) = await CartAsync(chairs: 10);
        var preview = await PreviewAsync(furniture, code, chairs: 10);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: 20m));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await furniture.Admin.GetFromJsonAsync<AdminOrderView>($"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}", CancellationToken);

        Assert.Equal(20m, stored!.ShippingPrice);
        Assert.Equal([(1, 7_100, 10_000, 12m), (2, 2_000, 3_000, 8m)], stored.Parcels.Select(parcel => (parcel.Number, parcel.GrossWeightGrams, parcel.BandUpToGrams, parcel.Price)));
        Assert.Equal(
            preview.Parcels.Select(parcel => (parcel.GrossWeightGrams, parcel.BandUpToGrams, parcel.Price, string.Join(",", parcel.Contents.Select(item => $"{item.Sku}x{item.Quantity}")))),
            stored.Parcels.Select(parcel => (parcel.GrossWeightGrams, parcel.BandUpToGrams, parcel.Price, string.Join(",", parcel.Contents.Select(item => $"{item.Sku}x{item.Quantity}")))));
        Assert.Equal($"{FurnitureStore.SkuOf("oak-chair")}x8", string.Join(",", stored.Parcels[0].Contents.Select(item => $"{item.Sku}x{item.Quantity}")));

        // The parcels are kept beside each order, so another order by the same van keeps its own and leaves these alone.
        var (_, _, another) = await CartAsync(chairs: 1, furniture, code);
        using var placedToo = await another.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: 8m));
        var orderToo = await another.ReadAsync<PlacedOrder>(placedToo, HttpStatusCode.Created);
        var storedToo = await furniture.Admin.GetFromJsonAsync<AdminOrderView>($"/api/admin/stores/{furniture.Store.StoreId}/orders/{orderToo.Number}", CancellationToken);
        var storedAgain = await furniture.Admin.GetFromJsonAsync<AdminOrderView>($"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}", CancellationToken);

        Assert.Equal([(1, 1_150)], storedToo!.Parcels.Select(parcel => (parcel.Number, parcel.GrossWeightGrams)));
        Assert.Equal([7_100, 2_000], storedAgain!.Parcels.Select(parcel => parcel.GrossWeightGrams));
    }

    // A price by the parcel moves with the cart and with prices the store can change at any moment, so the shopper says
    // which one they agreed to, and a different one is refused with the new quote.
    [Fact]
    public async Task A_price_by_the_parcel_is_charged_only_as_the_shopper_saw_it()
    {
        var (furniture, code, shopper) = await CartAsync(chairs: 10);

        using var unsaid = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: null));
        using var cheaper = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: 19m));
        var shown = (await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods?country=CZ")).ShippingMethods.Single(method => method.Code == code).Price;
        await OwnVan.PriceAsync(furniture, code, ("own", 3_000, 9m), ("own", 10_000, 13m));
        using var raced = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: shown));
        var moved = await raced.Content.ReadFromJsonAsync<PriceProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, unsaid.StatusCode);
        Assert.Contains("expectedShippingPrice", (await unsaid.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!.Errors.Keys);
        Assert.Equal(HttpStatusCode.Conflict, cheaper.StatusCode);
        Assert.Equal(20m, shown);
        Assert.Equal(HttpStatusCode.Conflict, raced.StatusCode);
        Assert.Equal((22m, 2), (moved!.ShippingQuote.Total, moved.ShippingQuote.Parcels.Count));
    }

    [Fact]
    public async Task A_method_that_became_unavailable_is_refused_with_its_reason()
    {
        var (furniture, code, shopper) = await CartAsync(chairs: 10);
        using var settled = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-settings", new { PackagingWeightGrams = 300, FillPercent = 70, LargestParcelOrder = 1 }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);

        using var refused = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: 20m));
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(["This order is too large to send by parcel. Please contact the store about delivering it."], problem!.Errors["shippingMethodCode"]);
    }

    // A flat method's checkout does not change: no price need be sent, and one that is sent is held to.
    [Fact]
    public async Task A_flat_methods_checkout_is_as_it_was()
    {
        var (_, _, shopper) = await CartAsync(chairs: 1);

        using var unsaid = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout("courier", expectedShippingPrice: null));
        var order = await shopper.ReadAsync<PlacedOrder>(unsaid, HttpStatusCode.Created);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var (_, _, another) = await CartAsync(chairs: 1);
        using var wrong = await another.PostAsync("/api/storefront/checkout", OwnVan.Checkout("courier", expectedShippingPrice: 5m));

        Assert.Equal(4.90m, confirmation.ShippingPrice);
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
    }

    // A free-shipping code takes the computed price off, and the invoice and a full refund carry it.
    [Fact]
    public async Task The_computed_price_is_what_a_code_takes_off_the_invoice_shows_and_a_refund_gives_back()
    {
        var (furniture, code, shopper) = await CartAsync(chairs: 10);
        using var discount = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/discounts",
            new { Code = "VANFREE", Name = "Free van", Kind = "FreeShipping", Value = 0m, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, discount.StatusCode);
        var (_, _, discounted) = await CartAsync(chairs: 10, furniture, code);
        using var applied = await discounted.PutAsync("/api/storefront/cart/discount", new { Code = "VANFREE" });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        using var free = await discounted.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: 20m));
        var freeOrder = await discounted.ReadAsync<PlacedOrder>(free, HttpStatusCode.Created);
        var freeConfirmation = await discounted.GetJsonAsync<OrderView>($"/api/storefront/orders/{freeOrder.Number}?token={freeOrder.Token}");
        using var placed = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: 20m));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var invoice = await factory.EventuallyAsync(
            () => factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Invoice>()
                .SingleOrDefaultAsync(candidate => candidate.OrderNumber == order.Number && candidate.Kind == InvoiceKind.Invoice, CancellationToken)),
            found => found is not null,
            CancellationToken);
        using var refunded = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        var afterwards = await refunded.Content.ReadFromJsonAsync<AdminOrderView>(CancellationToken);

        Assert.Equal((0m, "VANFREE", 20m), (freeConfirmation.ShippingPrice, freeConfirmation.Discount!.Code, freeConfirmation.Discount.Amount));
        Assert.Contains(invoice!.Lines, line => line.Description == OwnVan.MethodName && line.UnitPrice == 20m && line.Quantity == 1);
        Assert.Equal(1_020m, invoice.Total);
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal(1_020m, Assert.Single(afterwards!.Returns).RefundedAmount);
    }

    // An empty cart is nothing to estimate parcels for, and nothing to weigh against a limit, so only a flat method
    // without one answers it. Nothing is listed apart: nothing in the cart is the shopper's to change.
    [Fact]
    public async Task An_empty_cart_is_offered_no_method_priced_by_the_parcel()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await OwnVan.CreateAsync(factory, furniture);
        using var limited = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods",
            new { Name = "Light courier", ProviderKey = "manual", Price = 3m, VatRate = 21m, IsActive = true, RequiresPickupPoint = false, MaxWeightGrams = 5_000, Countries = new[] { "CZ" } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, limited.StatusCode);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        var methods = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods?country=CZ");

        Assert.Equal(["courier"], methods.ShippingMethods.Select(method => method.Code));
        Assert.Empty(methods.UnavailableShippingMethods);
    }

    // "Delivery from": the cheapest method on offer, flat or by the parcel, before any checkout. A method whose service
    // is off the road, or whose prices are invented, is not on offer.
    [Fact]
    public async Task The_store_says_what_delivery_costs_at_the_least()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (code, service) = await OwnVan.CreateWithServiceAsync(factory, furniture);
        var (_, _, shopper) = await CartAsync(chairs: 1, furniture, code);

        var withCourier = await shopper.GetJsonAsync<StoreView>("/api/storefront/store");
        using var courierOff = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/courier",
            new { Name = "Courier", Price = 4.90m, VatRate = 21m, IsActive = false, RequiresPickupPoint = false },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, courierOff.StatusCode);
        var byTheVan = await shopper.GetJsonAsync<StoreView>("/api/storefront/store");
        await OwnVan.PriceAsync(furniture, code, ("placeholder", 3_000, 1m));
        var invented = await shopper.GetJsonAsync<StoreView>("/api/storefront/store");
        await OwnVan.PriceAsync(furniture, code, ("own", 3_000, 8m));
        await OwnVan.SwitchOffAsync(factory, service);
        var offTheRoad = await shopper.GetJsonAsync<StoreView>("/api/storefront/store");

        Assert.Equal(4.90m, withCourier.DeliveryFrom);
        Assert.Equal(8m, byTheVan.DeliveryFrom);
        Assert.Null(invented.DeliveryFrom);
        Assert.Null(offTheRoad.DeliveryFrom);
    }

    // Invented prices never reach a shopper, whatever readiness said when the store opened (D-219).
    [Fact]
    public async Task A_method_on_placeholder_prices_is_not_offered()
    {
        var (furniture, code, shopper) = await CartAsync(chairs: 1);
        await OwnVan.PriceAsync(furniture, code, ("placeholder", 3_000, 8m), ("placeholder", 10_000, 12m));

        var methods = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods?country=CZ");
        using var refused = await shopper.PostAsync("/api/storefront/checkout", OwnVan.Checkout(code, expectedShippingPrice: 8m));

        Assert.DoesNotContain(code, methods.ShippingMethods.Select(method => method.Code));
        Assert.DoesNotContain(code, methods.UnavailableShippingMethods.Select(method => method.Code));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // However long the cart, the methods cost the same few queries (D-184's habit).
    [Fact]
    public async Task The_methods_cost_the_same_few_queries_however_long_the_cart()
    {
        var (furniture, _, shopper) = await CartAsync(chairs: 1);
        var one = await CountedMethodsAsync(shopper);
        foreach (var listing in furniture.Products.Values.Where(listing => listing != furniture.Products["oak-chair"]))
        {
            using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = listing, Quantity = 3 });
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }

        var every = await CountedMethodsAsync(shopper);

        Assert.True(one > 0);
        Assert.Equal(one, every);
    }

    private async Task<(FurnitureStore Furniture, string Code, StorefrontApi Shopper)> CartAsync(int chairs, FurnitureStore? furniture = null, string? code = null)
    {
        furniture ??= await FurnitureStore.CreateAsync(factory);
        code ??= await OwnVan.CreateAsync(factory, furniture);
        var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = chairs });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        return (furniture, code, shopper);
    }

    // Another thing in the cart, measured as given or, for a form from before the rule, not at all.
    private async Task<Guid> ListedAsync(FurnitureStore furniture, StorefrontApi shopper, int weight, int length, int width, int height, bool unsize = false)
    {
        var productId = await furniture.Admin.CreateProductAsync();
        using var measured = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}", new { Ean = (string?)null, WeightGrams = weight, LengthMm = length, WidthMm = width, HeightMm = height }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, measured.StatusCode);
        var listing = await furniture.Admin.ListProductAsync(furniture.Store.StoreId, productId, $"Thing {Guid.NewGuid():N}"[..14], 10m);
        await furniture.Admin.StockAsync(productId, 5);

        if (unsize)
        {
            await FormsFromBefore.UnsizeAsync(factory, productId);
        }

        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = listing, Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        return await furniture.Admin.DefaultVariantIdAsync(productId);
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

    private sealed record MethodsView(List<MethodView> ShippingMethods, List<UnavailableView> UnavailableShippingMethods);

    private sealed record MethodView(string Code, string Name, decimal Price, string Pricing, int? Parcels, MapParcelView? MapParcel);

    private sealed record MapParcelView(decimal WeightKg, int? LengthCm, int? WidthCm, int? DepthCm);

    private sealed record UnavailableView(string Code, string Name, string Reason, Guid? VariantId);

    private sealed record QuoteView(List<ParcelView> Parcels);

    private sealed record ParcelView(List<ContentView> Contents, int GrossWeightGrams, int BandUpToGrams, decimal Price);

    private sealed record ContentView(string Sku, int Quantity);

    private sealed record AdminOrderView(decimal ShippingPrice, List<AdminParcelView> Parcels, List<AdminReturnView> Returns);

    private sealed record AdminParcelView(int Number, int GrossWeightGrams, int BandUpToGrams, decimal Price, List<ContentView> Contents);

    private sealed record AdminReturnView(decimal RefundedAmount);

    private sealed record PriceProblemView(QuoteTotalView ShippingQuote);

    private sealed record QuoteTotalView(decimal Total, List<ParcelView> Parcels);

    private sealed record StoreView(decimal? DeliveryFrom);

    private sealed record ProblemView(Dictionary<string, string[]> Errors);
}
