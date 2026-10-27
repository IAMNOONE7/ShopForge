using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Shipping;

namespace ShopForge.IntegrationTests.Shipping;

// What each carrier service takes is configuration kept in one place, no store's method promises more than its
// service takes, and the few numbers a parcel estimate needs are the store's to set (D-218). Every test that changes
// a service makes one of its own: the catalogue is the platform's, shared by every test at once.
public sealed class ParcelMethodTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Packetas_services_are_seeded_with_where_their_limits_were_read()
    {
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);

        var services = (await operatorClient.GetFromJsonAsync<List<ServiceView>>("/api/platform/carrier-services", CancellationToken))!;
        var zbox = services.Single(service => service.Key == "packeta-cz-zbox");
        var doorstep = services.Single(service => service.Key == "packeta-cz-home");

        Assert.Equal(("packeta", "locker", "CZ", true, 15_000, (int?)null, 90_300_000L, (int?)1), (zbox.ProviderKey, zbox.Kind, zbox.Country, zbox.IsActive, zbox.MaxWeightGrams, zbox.MaxSumOfSidesMm, zbox.LargestVolumeMm3, zbox.MaxParcels));
        Assert.Equal(new SizeView(600, 430, 350), zbox.Largest);
        Assert.Equal(("packeta", "home", "CZ", true, 5_000, (int?)1_200, 64_000_000L, (int?)null), (doorstep.ProviderKey, doorstep.Kind, doorstep.Country, doorstep.IsActive, doorstep.MaxWeightGrams, doorstep.MaxSumOfSidesMm, doorstep.LargestVolumeMm3, doorstep.MaxParcels));
        Assert.Equal(new SizeView(700, 700, 700), doorstep.Largest);
        Assert.All([zbox, doorstep], service =>
        {
            Assert.Equal(new SizeView(100, 70, 10), service.Smallest);
            Assert.StartsWith("docs.packeta.com/docs/destination-country/cz/", service.Source, StringComparison.Ordinal);
            Assert.Equal(new DateOnly(2026, 10, 24), service.SourceDate);
        });
    }

    [Fact]
    public async Task Only_an_operator_keeps_the_catalogue()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var anonymous = factory.CreateClient();

        using var asMerchant = await furniture.Admin.GetAsync("/api/platform/carrier-services", CancellationToken);
        using var asNobody = await anonymous.GetAsync("/api/platform/carrier-services", CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, asMerchant.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asNobody.StatusCode);
    }

    [Fact]
    public async Task The_operator_adds_and_changes_a_service_and_both_are_audited()
    {
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        var key = UniqueKey();

        using var added = await operatorClient.PostAsJsonAsync("/api/platform/carrier-services", NewService(key), CancellationToken);
        using var again = await operatorClient.PostAsJsonAsync("/api/platform/carrier-services", NewService(key), CancellationToken);
        using var changed = await operatorClient.PutAsJsonAsync(
            $"/api/platform/carrier-services/{key}",
            Terms() with { MaxWeightGrams = 20_000, Source = "The courier's price list of November 2026." },
            CancellationToken);
        var service = (await operatorClient.GetFromJsonAsync<List<ServiceView>>("/api/platform/carrier-services", CancellationToken))!.Single(candidate => candidate.Key == key);
        var trail = await operatorClient.GetFromJsonAsync<List<AuditView>>("/api/platform/audit?action=carrier-service.changed&pageSize=200", CancellationToken);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal((20_000, "The courier's price list of November 2026."), (service.MaxWeightGrams, service.Source));
        var entry = Assert.Single(trail!, candidate => candidate.Subject == key);
        Assert.Contains("\"maxWeightGrams\":10000", entry.Details!, StringComparison.Ordinal);
        Assert.Contains("\"maxWeightGrams\":20000", entry.Details!, StringComparison.Ordinal);
    }

    // The largest parcel here is 80 × 60 × 60 cm with sides adding up to at most 2 m. A cube of 40 cm on each side is the
    // roomiest box whose sides add up to 1.2 m.
    [Theory]
    [InlineData("smallest", "smallest")]
    [InlineData("beyond the sides", "largestVolumeMm3")]
    [InlineData("beyond the sum", "largestVolumeMm3")]
    [InlineData("parcels", "maxParcels")]
    [InlineData("source", "source")]
    [InlineData("provider", "providerKey")]
    [InlineData("key", "key")]
    public async Task A_service_that_could_take_nothing_is_refused(string wrong, string field)
    {
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        var service = NewService(UniqueKey());
        service = wrong switch
        {
            "smallest" => service with { Smallest = new SizeView(900, 70, 10) },
            "beyond the sides" => service with { LargestVolumeMm3 = 800L * 600 * 600 + 1 },
            "beyond the sum" => service with { MaxSumOfSidesMm = 1_200, LargestVolumeMm3 = 400L * 400 * 400 + 1 },
            "parcels" => service with { MaxParcels = 0 },
            "source" => service with { Source = " " },
            "provider" => service with { ProviderKey = "pigeon" },
            _ => service with { Key = "Not-A-Key" },
        };

        using var refused = await operatorClient.PostAsJsonAsync("/api/platform/carrier-services", service, CancellationToken);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal([field], problem!.Errors.Keys);
    }

    // What a merchant's method can send by: the services in use, of providers this platform offers its stores.
    [Fact]
    public async Task A_merchant_chooses_from_the_services_in_use()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var unused = await ServiceAsync(isActive: false);

        var services = await furniture.Admin.GetFromJsonAsync<List<ServiceView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/carrier-services", CancellationToken);

        Assert.Contains(services!, service => service.Key == "packeta-cz-zbox");
        Assert.Contains(services!, service => service.Key == "packeta-cz-home");
        Assert.DoesNotContain(services!, service => service.Key == unused);
    }

    [Fact]
    public async Task A_method_priced_by_the_parcel_sends_within_its_service()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var created = await CreateMethodAsync(furniture, ZBoxMethod() with { ParcelMaxWeightGrams = 10_000, ParcelLengthMm = 300, ParcelWidthMm = 400, ParcelHeightMm = 350 });
        var method = await created.Content.ReadFromJsonAsync<MethodView>(CancellationToken);
        var trail = await furniture.Admin.GetFromJsonAsync<List<AuditView>>("/api/admin/audit?action=shipping-method.added", CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("perParcel", "packeta-cz-zbox", (int?)10_000), (method!.Pricing, method.CarrierService, method.ParcelMaxWeightGrams));
        Assert.Equal(((int?)400, (int?)350, (int?)300), (method.ParcelLengthMm, method.ParcelWidthMm, method.ParcelHeightMm));
        Assert.Contains(trail!, entry => entry.Subject == "Z-BOX by the parcel" && entry.Details!.Contains("PerParcel", StringComparison.Ordinal));
    }

    // Each limit beyond its service is refused with both numbers, so the merchant knows what the carrier takes.
    [Theory]
    [InlineData("weight", "parcelMaxWeightGrams", "takes parcels up to 15000 g; 15001 g is more than that")]
    [InlineData("larger", "parcelLengthMm", "takes parcels up to 600 × 430 × 350 mm, longest side first; 601 × 400 × 300 mm is larger than that")]
    [InlineData("smaller", "parcelLengthMm", "takes parcels of at least 100 × 70 × 10 mm; 90 × 70 × 10 mm is smaller than that")]
    [InlineData("half", "parcelHeightMm", "go together")]
    [InlineData("sum", "parcelLengthMm", "takes parcels whose sides add up to at most 1200 mm; these add up to 1300 mm")]
    public async Task A_parcel_limit_beyond_the_service_is_refused_naming_both(string beyond, string field, string message)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var request = beyond switch
        {
            "weight" => ZBoxMethod() with { ParcelMaxWeightGrams = 15_001 },
            "larger" => ZBoxMethod() with { ParcelLengthMm = 601, ParcelWidthMm = 400, ParcelHeightMm = 300 },
            "smaller" => ZBoxMethod() with { ParcelLengthMm = 90, ParcelWidthMm = 70, ParcelHeightMm = 10 },
            "half" => ZBoxMethod() with { ParcelLengthMm = 300, ParcelWidthMm = 200 },
            _ => DoorstepMethod() with { ParcelLengthMm = 700, ParcelWidthMm = 400, ParcelHeightMm = 200 },
        };

        using var refused = await CreateMethodAsync(furniture, request);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(message, Assert.Single(problem!.Errors[field]), StringComparison.Ordinal);
    }

    // What makes a method fit its service: one to send by, in use, of its own provider, to its country and its kind of
    // place, limiting each parcel rather than the cart; and parcel limits only on a method priced by the parcel.
    [Theory]
    [InlineData("none", "carrierService")]
    [InlineData("unknown", "carrierService")]
    [InlineData("unused", "carrierService")]
    [InlineData("provider", "carrierService")]
    [InlineData("door", "requiresPickupPoint")]
    [InlineData("country", "countries")]
    [InlineData("cart", "maxWeightGrams")]
    [InlineData("flat", "parcelMaxWeightGrams")]
    public async Task A_method_that_does_not_fit_its_service_is_refused(string mismatch, string field)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var request = mismatch switch
        {
            "none" => ZBoxMethod() with { CarrierService = null },
            "unknown" => ZBoxMethod() with { CarrierService = "nobody-sends-this" },
            "unused" => ZBoxMethod() with { CarrierService = await ServiceAsync(isActive: false, providerKey: "packeta", kind: "locker") },
            "provider" => ZBoxMethod() with { ProviderKey = "manual" },
            "door" => ZBoxMethod() with { RequiresPickupPoint = false },
            "country" => ZBoxMethod() with { Countries = ["SK"] },
            "cart" => ZBoxMethod() with { MaxWeightGrams = 5_000 },
            _ => ZBoxMethod() with { Pricing = "flat", ParcelMaxWeightGrams = 1_000 },
        };

        using var refused = await CreateMethodAsync(furniture, request);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal([field], problem!.Errors.Keys);
    }

    // The delivery terms come together: a save from a form that knows nothing of them keeps them, and limits sent
    // without the pricing are refused rather than half applied.
    [Fact]
    public async Task A_save_that_leaves_out_the_pricing_keeps_the_delivery_terms()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var created = await CreateMethodAsync(furniture, ZBoxMethod() with { ParcelMaxWeightGrams = 9_000 });
        var code = (await created.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!.Code;

        using var renamed = await UpdateMethodAsync(furniture, code, ZBoxMethod() with { Name = "Z-BOX renamed", Pricing = null, CarrierService = null });
        using var halfSent = await UpdateMethodAsync(furniture, code, ZBoxMethod() with { Pricing = null, CarrierService = null, ParcelMaxWeightGrams = 8_000 });
        var method = (await renamed.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!;

        Assert.Equal(("Z-BOX renamed", "perParcel", "packeta-cz-zbox", (int?)9_000), (method.Name, method.Pricing, method.CarrierService, method.ParcelMaxWeightGrams));
        Assert.Equal(HttpStatusCode.BadRequest, halfSent.StatusCode);
        Assert.Equal(["pricing"], (await halfSent.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!.Errors.Keys);
    }

    // A method priced by the parcel with no prices yet is not offered and cannot be ordered with; whatever its prices, a
    // feed publishes no rate for it (D-220). It counts as a method the store has, and its own check names what it lacks.
    // The store's own delivery needs no account, so nothing but the missing prices keeps it from the checkout.
    [Fact]
    public async Task A_method_priced_by_the_parcel_without_prices_is_not_offered_and_no_feed_publishes_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var created = await CreateMethodAsync(furniture, OwnDeliveryMethod(await ServiceAsync(isActive: true)));
        var code = (await created.Content.ReadFromJsonAsync<MethodView>(CancellationToken))!.Code;
        using var courierOff = await UpdateMethodAsync(furniture, "courier", new MethodRequest("Courier", null, 4.90m, 21m, false, false));
        Assert.Equal(HttpStatusCode.OK, courierOff.StatusCode);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var methods = await shopper.GetJsonAsync<CheckoutMethodsView>("/api/storefront/checkout/methods?country=CZ");
        using var ordered = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(shipping: code));
        var unpriced = await shopper.GetJsonAsync<StoreView>("/api/storefront/store");
        using var priced = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/rates",
            new { Source = "own", Bands = new[] { new { UpToGrams = 10_000, Price = 8m } } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);
        await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
        var rates = await scope.ServiceProvider.GetRequiredService<IStoreShippingRates>().FindAsync(CancellationToken);
        var readiness = await ReadinessAsync(furniture);

        Assert.DoesNotContain(code, methods.ShippingMethods.Select(method => method.Code));
        Assert.Equal(HttpStatusCode.BadRequest, ordered.StatusCode);
        Assert.Contains("shippingMethodCode", (await ordered.Content.ReadFromJsonAsync<ProblemView>(CancellationToken))!.Errors.Keys);
        Assert.DoesNotContain(rates, rate => rate.Service == "Own van by the parcel");
        Assert.Null(unpriced.DeliveryFrom);
        Assert.True(readiness.Single(check => check.Check == "orders.shipping-method").IsReady);
    }

    [Fact]
    public async Task Readiness_names_a_method_priced_by_the_parcel_that_could_carry_nothing()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        var key = await ServiceAsync(isActive: true);

        var before = await ParcelCheckAsync(furniture);
        using var created = await CreateMethodAsync(furniture, OwnDeliveryMethod(key) with { ParcelMaxWeightGrams = 1_000 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var priced = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/own-van-by-the-parcel/rates",
            new { Source = "own", Bands = new[] { new { UpToGrams = 1_000, Price = 5m } } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);
        var fits = await ParcelCheckAsync(furniture);
        await SettingsAsync(furniture, packagingWeightGrams: 1_000);
        var heavyPackaging = await ParcelCheckAsync(furniture);
        await SettingsAsync(furniture, packagingWeightGrams: 300);
        using var switchedOff = await operatorClient.PutAsJsonAsync($"/api/platform/carrier-services/{key}", Terms() with { IsActive = false }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, switchedOff.StatusCode);
        var unused = await ParcelCheckAsync(furniture);

        Assert.True(before.IsReady);
        Assert.True(fits.IsReady);
        Assert.Equal("required", heavyPackaging.Weight);
        Assert.Contains("\"Own van by the parcel\" sends parcels of at most 1000 g, and the store's packaging alone weighs 1000 g", heavyPackaging.Problem!, StringComparison.Ordinal);
        Assert.Contains("\"Own van by the parcel\" sends by a carrier service that is not in use", unused.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shipping_settings_start_at_the_owners_defaults_and_each_change_is_audited()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var path = $"/api/admin/stores/{furniture.Store.StoreId}/shipping-settings";

        var defaults = await furniture.Admin.GetFromJsonAsync<SettingsView>(path, CancellationToken);
        using var changed = await furniture.Admin.PutAsJsonAsync(path, new SettingsView(450, 80, 20), CancellationToken);
        using var restated = await furniture.Admin.PutAsJsonAsync(path, new SettingsView(450, 80, 20), CancellationToken);
        var stored = await furniture.Admin.GetFromJsonAsync<SettingsView>(path, CancellationToken);
        var trail = await furniture.Admin.GetFromJsonAsync<List<AuditView>>("/api/admin/audit?action=shipping.settings-changed", CancellationToken);

        Assert.Equal(new SettingsView(300, 70, 10), defaults);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restated.StatusCode);
        Assert.Equal(new SettingsView(450, 80, 20), stored);
        var entry = Assert.Single(trail!);
        Assert.Contains("\"packagingWeightGrams\":300", entry.Details!, StringComparison.Ordinal);
        Assert.Contains("\"packagingWeightGrams\":450", entry.Details!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 70, 10, "packagingWeightGrams")]
    [InlineData(10_001, 70, 10, "packagingWeightGrams")]
    [InlineData(300, 0, 10, "fillPercent")]
    [InlineData(300, 101, 10, "fillPercent")]
    [InlineData(300, 70, 0, "largestParcelOrder")]
    [InlineData(300, 70, 101, "largestParcelOrder")]
    public async Task Shipping_settings_that_make_no_sense_are_refused(int packagingWeightGrams, int fillPercent, int largestParcelOrder, string field)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-settings",
            new SettingsView(packagingWeightGrams, fillPercent, largestParcelOrder),
            CancellationToken);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal([field], problem!.Errors.Keys);
    }

    private static string UniqueKey() => $"test-{Guid.NewGuid():N}"[..24];

    private static ServiceTerms Terms() =>
        new("Own van, Czechia", true, 10_000, new SizeView(800, 600, 600), 2_000, new SizeView(50, 50, 10), 200_000_000L, null, "Our own van, measured.", new DateOnly(2026, 10, 25));

    private static NewServiceRequest NewService(string key) =>
        new(key, "manual", "home", "CZ", "Own van, Czechia", true, 10_000, new SizeView(800, 600, 600), 2_000, new SizeView(50, 50, 10), 200_000_000L, null, "Our own van, measured.", new DateOnly(2026, 10, 25));

    private async Task<string> ServiceAsync(bool isActive, string providerKey = "manual", string kind = "home")
    {
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        var key = UniqueKey();

        using var added = await operatorClient.PostAsJsonAsync(
            "/api/platform/carrier-services",
            NewService(key) with { ProviderKey = providerKey, Kind = kind, IsActive = isActive },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        return key;
    }

    private static MethodRequest ZBoxMethod() =>
        new("Z-BOX by the parcel", "packeta", 0m, 21m, true, true, Pricing: "perParcel", CarrierService: "packeta-cz-zbox");

    private static MethodRequest DoorstepMethod() =>
        new("Doorstep by the parcel", "packeta", 0m, 21m, true, false, Countries: ["CZ"], Pricing: "perParcel", CarrierService: "packeta-cz-home");

    // It names its country, as a method must for a feed to publish a rate for it at all.
    private static MethodRequest OwnDeliveryMethod(string key) =>
        new("Own van by the parcel", "manual", 0m, 21m, true, false, Countries: ["CZ"], Pricing: "perParcel", CarrierService: key);

    private static async Task<HttpResponseMessage> CreateMethodAsync(FurnitureStore furniture, MethodRequest request) =>
        await furniture.Admin.PostAsJsonAsync($"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods", request, CancellationToken);

    private static async Task<HttpResponseMessage> UpdateMethodAsync(FurnitureStore furniture, string code, MethodRequest request) =>
        await furniture.Admin.PutAsJsonAsync($"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}", request, CancellationToken);

    private static async Task SettingsAsync(FurnitureStore furniture, int packagingWeightGrams)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-settings", new SettingsView(packagingWeightGrams, 70, 10), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private static async Task<List<CheckView>> ReadinessAsync(FurnitureStore furniture) =>
        (await furniture.Admin.GetFromJsonAsync<ReadinessView>($"/api/admin/stores/{furniture.Store.StoreId}/readiness", CancellationToken))!.Checks;

    private static async Task<CheckView> ParcelCheckAsync(FurnitureStore furniture) =>
        (await ReadinessAsync(furniture)).Single(check => check.Check == "orders.parcel-methods");

    private sealed record SizeView(int LengthMm, int WidthMm, int HeightMm);

    private sealed record ServiceView(
        string Key,
        string ProviderKey,
        string Name,
        string Kind,
        string Country,
        bool IsActive,
        int MaxWeightGrams,
        SizeView Largest,
        int? MaxSumOfSidesMm,
        SizeView Smallest,
        long LargestVolumeMm3,
        int? MaxParcels,
        string Source,
        DateOnly SourceDate);

    private sealed record ServiceTerms(
        string Name,
        bool IsActive,
        int MaxWeightGrams,
        SizeView Largest,
        int? MaxSumOfSidesMm,
        SizeView Smallest,
        long LargestVolumeMm3,
        int? MaxParcels,
        string Source,
        DateOnly SourceDate);

    private sealed record NewServiceRequest(
        string Key,
        string ProviderKey,
        string Kind,
        string Country,
        string Name,
        bool IsActive,
        int MaxWeightGrams,
        SizeView Largest,
        int? MaxSumOfSidesMm,
        SizeView Smallest,
        long LargestVolumeMm3,
        int? MaxParcels,
        string Source,
        DateOnly SourceDate);

    private sealed record MethodRequest(
        string Name,
        string? ProviderKey,
        decimal Price,
        decimal VatRate,
        bool IsActive,
        bool RequiresPickupPoint,
        int? MaxWeightGrams = null,
        string[]? Countries = null,
        string? Pricing = null,
        string? CarrierService = null,
        int? ParcelMaxWeightGrams = null,
        int? ParcelLengthMm = null,
        int? ParcelWidthMm = null,
        int? ParcelHeightMm = null);

    private sealed record MethodView(
        string Code,
        string Name,
        string Pricing,
        string? CarrierService,
        int? ParcelMaxWeightGrams,
        int? ParcelLengthMm,
        int? ParcelWidthMm,
        int? ParcelHeightMm);

    private sealed record SettingsView(int PackagingWeightGrams, int FillPercent, int LargestParcelOrder);

    private sealed record CheckoutMethodsView(List<CheckoutMethodView> ShippingMethods);

    private sealed record StoreView(decimal? DeliveryFrom);

    private sealed record CheckoutMethodView(string Code);

    private sealed record ReadinessView(List<CheckView> Checks);

    private sealed record CheckView(string Check, bool IsReady, string? Problem, string Weight);

    private sealed record ProblemView(Dictionary<string, string[]> Errors);

    private sealed record AuditView(string Action, string Subject, string? Details);
}
