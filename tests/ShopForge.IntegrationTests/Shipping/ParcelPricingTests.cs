using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Shipping;

// A method priced by the parcel has a rate card, starting from the owner's placeholder prices, and the merchant can ask
// what any cart costs by any method without a shopper: the quote the checkout will ask (D-219). The furniture store's
// products are 850 g each in a 450 × 400 × 90 mm box.
public sealed class ParcelPricingTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_method_priced_by_the_parcel_starts_on_the_placeholder_prices_only_in_korunas()
    {
        var koruna = await TestStores.CreateInCurrencyAsync(factory.Services, "CZK");
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, koruna.TenantId));
        var euro = await FurnitureStore.CreateAsync(factory);

        using var inKorunas = await admin.PostAsJsonAsync($"/api/admin/stores/{koruna.StoreId}/shipping-methods", ZBoxMethod(), CancellationToken);
        using var inEuros = await euro.Admin.PostAsJsonAsync($"/api/admin/stores/{euro.Store.StoreId}/shipping-methods", ZBoxMethod(), CancellationToken);
        var placeholder = (await inKorunas.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!;
        var unpriced = (await inEuros.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!;
        var refused = await ParcelCheckAsync(admin, koruna.StoreId);
        var unready = await ParcelCheckAsync(euro.Admin, euro.Store.StoreId);

        Assert.Equal("placeholder", placeholder.RatesFrom);
        Assert.Equal([new BandView(2_000, 79m), new BandView(5_000, 99m), new BandView(10_000, 129m), new BandView(15_000, 159m)], placeholder.RateBands);
        Assert.Null(unpriced.RatesFrom);
        Assert.Empty(unpriced.RateBands);
        Assert.Contains("\"Z-BOX by the parcel\" still has the placeholder prices, which are invented", refused.Problem!, StringComparison.Ordinal);
        Assert.Contains("\"Z-BOX by the parcel\" has no prices yet", unready.Problem!, StringComparison.Ordinal);
    }

    // Moved to another service, a method still on placeholder prices takes that service's placeholder prices.
    [Fact]
    public async Task A_method_moved_to_another_service_takes_that_services_placeholder_prices()
    {
        var koruna = await TestStores.CreateInCurrencyAsync(factory.Services, "CZK");
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, koruna.TenantId));
        using var created = await admin.PostAsJsonAsync($"/api/admin/stores/{koruna.StoreId}/shipping-methods", ZBoxMethod(), CancellationToken);
        var code = (await created.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!.Code;

        using var moved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{koruna.StoreId}/shipping-methods/{code}",
            new { Name = "Z-BOX by the parcel", ProviderKey = "packeta", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = false, Pricing = "perParcel", CarrierService = "packeta-cz-home" },
            CancellationToken);
        var method = (await moved.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!;

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal("placeholder", method.RatesFrom);
        Assert.Equal([new BandView(2_000, 109m), new BandView(5_000, 129m)], method.RateBands);
    }

    // The whole card at once, from the carrier's price list of a date, audited with its before and after.
    [Fact]
    public async Task Prices_of_the_merchants_own_replace_the_placeholder_and_let_the_store_open()
    {
        var koruna = await TestStores.CreateInCurrencyAsync(factory.Services, "CZK");
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, koruna.TenantId));
        using var created = await admin.PostAsJsonAsync($"/api/admin/stores/{koruna.StoreId}/shipping-methods", ZBoxMethod(), CancellationToken);
        var code = (await created.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!.Code;

        using var priced = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{koruna.StoreId}/shipping-methods/{code}/rates",
            new RatesRequest("carrier", new DateOnly(2026, 11, 1), [new BandView(5_000, 89m), new BandView(15_000, 139m)]),
            CancellationToken);
        var method = (await priced.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!;
        var check = await ParcelCheckAsync(admin, koruna.StoreId);
        var trail = await admin.GetFromJsonAsync<List<AuditView>>("/api/admin/audit?action=shipping-method.rates-changed", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);
        Assert.Equal(("carrier", new DateOnly(2026, 11, 1)), (method.RatesFrom, method.RatesPricedOn));
        Assert.Equal([new BandView(5_000, 89m), new BandView(15_000, 139m)], method.RateBands);
        Assert.True(check.IsReady, check.Problem);
        var entry = Assert.Single(trail!);
        Assert.Contains("\"source\":\"Placeholder\"", entry.Details!, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"Carrier\"", entry.Details!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("empty", "bands")]
    [InlineData("unordered", "bands")]
    [InlineData("weightless", "bands")]
    [InlineData("negative", "bands")]
    [InlineData("decimals", "bands")]
    [InlineData("sourceless", "source")]
    [InlineData("undated", "pricedOn")]
    [InlineData("flat", "pricing")]
    public async Task A_rate_card_that_makes_no_sense_is_refused(string wrong, string field)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var created = await furniture.Admin.PostAsJsonAsync($"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods", ZBoxMethod(), CancellationToken);
        var code = wrong == "flat" ? "courier" : (await created.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!.Code;
        var request = new RatesRequest("own", null, [new BandView(2_000, 4.90m), new BandView(5_000, 6.90m)]);
        request = wrong switch
        {
            "empty" => request with { Bands = [] },
            "unordered" => request with { Bands = [new BandView(5_000, 6.90m), new BandView(2_000, 4.90m)] },
            "weightless" => request with { Bands = [new BandView(0, 4.90m)] },
            "negative" => request with { Bands = [new BandView(2_000, -1m)] },
            "decimals" => request with { Bands = [new BandView(2_000, 4.999m)] },
            "sourceless" => request with { Source = null },
            "undated" => request with { Source = "carrier" },
            _ => request,
        };

        using var refused = await furniture.Admin.PutAsJsonAsync($"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/rates", request, CancellationToken);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal([field], problem!.Errors.Keys);
    }

    // Ten chairs: eight fill a parcel to its share of volume (8 × 16.2 litres of 140), and two go in another. Each parcel
    // is priced by the band its weight with the packaging falls in, and the quote is their sum.
    [Fact]
    public async Task The_preview_prices_each_parcel_by_its_band()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var code = await OwnVanAsync(furniture);
        var chair = furniture.Products["oak-chair"];

        var quote = await PreviewAsync(furniture, code, new QuoteRequest("CZ", [new LineView(chair, null, 10)]));
        var elsewhere = await PreviewAsync(furniture, code, new QuoteRequest("SK", [new LineView(chair, null, 1)]));
        var flat = await PreviewAsync(furniture, "courier", new QuoteRequest(null, [new LineView(chair, null, 10)]));

        Assert.True(quote.IsAvailable, quote.Unavailable?.Detail);
        Assert.Equal(("EUR", 20m), (quote.Currency, quote.Total));
        Assert.Equal([(7_100, 10_000, 12m), (2_000, 3_000, 8m)], quote.Parcels.Select(parcel => (parcel.GrossWeightGrams, parcel.BandUpToGrams, parcel.Price)));
        Assert.Equal([8, 2], quote.Parcels.Select(parcel => parcel.Contents.Single().Quantity));
        Assert.Equal(FurnitureStore.SkuOf("oak-chair"), quote.Parcels[0].Contents.Single().Sku);
        Assert.Equal("notServed", elsewhere.Unavailable!.Reason);
        Assert.Equal((4.90m, 0), (flat.Total, flat.Parcels.Count));
    }

    // Missing data makes the method unavailable, naming the form and what it lacks; a line that is no product on sale
    // here is refused outright.
    [Fact]
    public async Task The_preview_names_what_a_form_from_before_the_rule_lacks()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var code = await OwnVanAsync(furniture);
        await FormsFromBefore.UnsizeAsync(factory, furniture.ProductIds["beech-stool"]);

        var quote = await PreviewAsync(furniture, code, new QuoteRequest(null, [new LineView(furniture.Products["oak-chair"], null, 1), new LineView(furniture.Products["beech-stool"], null, 1)]));
        using var nothingOnSale = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/quote",
            new QuoteRequest(null, [new LineView(Guid.NewGuid(), null, 1)]),
            CancellationToken);

        Assert.Equal("missingSize", quote.Unavailable!.Reason);
        Assert.Equal($"{FurnitureStore.SkuOf("beech-stool")} has no length, width and height, so a parcel holding it cannot be priced.", quote.Unavailable.Detail);
        Assert.Equal(furniture.VariantIds["beech-stool"], quote.Unavailable.VariantId);
        Assert.Equal(HttpStatusCode.BadRequest, nothingOnSale.StatusCode);
        Assert.Equal(["lines"], (await nothingOnSale.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!.Errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task A_preview_line_holds_what_a_cart_line_can(int quantity)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/courier/quote",
            new QuoteRequest(null, [new LineView(furniture.Products["oak-chair"], null, quantity)]),
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(["lines"], (await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!.Errors.Keys);
    }

    // However long the cart, a quote reads the method, the products, the service and the packaging, and nothing per line
    // (D-184's habit).
    [Fact]
    public async Task A_quote_costs_the_same_few_queries_however_long_the_cart()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var code = await OwnVanAsync(furniture);

        var one = await CountedPreviewAsync(furniture, code, [new LineView(furniture.Products["oak-chair"], null, 1)]);
        var every = await CountedPreviewAsync(furniture, code, [.. furniture.Products.Values.Select(listing => new LineView(listing, null, 3))]);

        Assert.True(one > 0);
        Assert.Equal(one, every);
    }

    private async Task<string> OwnVanAsync(FurnitureStore furniture)
    {
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        var key = $"test-{Guid.NewGuid():N}"[..24];
        using var added = await operatorClient.PostAsJsonAsync(
            "/api/platform/carrier-services",
            new
            {
                Key = key,
                ProviderKey = "manual",
                Kind = "home",
                Country = "CZ",
                Name = "Own van, Czechia",
                IsActive = true,
                MaxWeightGrams = 10_000,
                Largest = new { LengthMm = 800, WidthMm = 600, HeightMm = 600 },
                MaxSumOfSidesMm = 2_000,
                Smallest = new { LengthMm = 50, WidthMm = 50, HeightMm = 10 },
                LargestVolumeMm3 = 200_000_000L,
                Source = "Our own van, measured.",
                SourceDate = new DateOnly(2026, 10, 25),
            },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        using var created = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods",
            new { Name = "Own van by the parcel", ProviderKey = "manual", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = false, Countries = new[] { "CZ" }, Pricing = "perParcel", CarrierService = key },
            CancellationToken);
        var code = (await created.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!.Code;
        using var priced = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/rates",
            new RatesRequest("own", null, [new BandView(3_000, 8m), new BandView(10_000, 12m)]),
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);

        return code;
    }

    private static object ZBoxMethod() =>
        new { Name = "Z-BOX by the parcel", ProviderKey = "packeta", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = true, Pricing = "perParcel", CarrierService = "packeta-cz-zbox" };

    private static async Task<QuoteView> PreviewAsync(FurnitureStore furniture, string code, QuoteRequest request)
    {
        using var response = await furniture.Admin.PostAsJsonAsync($"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/quote", request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<QuoteView>(CancellationToken))!;
    }

    private async Task<int> CountedPreviewAsync(FurnitureStore furniture, string code, IReadOnlyList<LineView> lines)
    {
        var tally = QueryCounter.NewTally();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/quote")
        {
            Content = JsonContent.Create(new QuoteRequest(null, lines)),
        };
        request.Headers.Add(QueryCounter.HeaderName, tally);

        using var response = await furniture.Admin.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return factory.Queries[tally];
    }

    private static async Task<CheckView> ParcelCheckAsync(HttpClient admin, Guid storeId) =>
        (await admin.GetFromJsonAsync<ReadinessView>($"/api/admin/stores/{storeId}/readiness", CancellationToken))!.Checks.Single(check => check.Check == "orders.parcel-methods");

    private sealed record BandView(int UpToGrams, decimal Price);

    private sealed record RatesRequest(string? Source, DateOnly? PricedOn, IReadOnlyList<BandView> Bands);

    private sealed record MethodView(string Code, string? RatesFrom, DateOnly? RatesPricedOn, List<BandView> RateBands);

    private sealed record LineView(Guid StoreProductId, Guid? VariantId, int Quantity);

    private sealed record QuoteRequest(string? Country, IReadOnlyList<LineView> Lines);

    private sealed record QuoteView(bool IsAvailable, string Currency, decimal Total, List<ParcelView> Parcels, RefusalView? Unavailable);

    private sealed record ParcelView(List<ContentView> Contents, int GrossWeightGrams, int BandUpToGrams, decimal Price, bool Alone);

    private sealed record ContentView(Guid VariantId, string Sku, int Quantity);

    private sealed record RefusalView(string Reason, string Detail, Guid? VariantId);

    private sealed record ReadinessView(List<CheckView> Checks);

    private sealed record CheckView(string Check, bool IsReady, string? Problem);

    private sealed record ProblemView(Dictionary<string, string[]> Errors);

    private sealed record AuditView(string Action, string Subject, string? Details);
}
