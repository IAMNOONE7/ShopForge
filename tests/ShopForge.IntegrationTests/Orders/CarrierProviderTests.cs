using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Shipping;

namespace ShopForge.IntegrationTests.Orders;

// A carrier whose points live in its own map, and methods that will not take every parcel. The store-operated
// provider is unchanged and its own tests say so; this is what the seam had to grow to fit a second one.
public sealed class CarrierProviderTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly MapCarrier _carrier = new();
    private readonly WebApplicationFactory<Program> _withCarrier;

    public CarrierProviderTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withCarrier = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IShippingProvider>(_carrier)));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withCarrier.Dispose();

    // Listing every box in the country is not what the widget model does, so it is not asked for one.
    [Fact]
    public async Task A_carrier_with_its_own_map_offers_no_list_and_is_not_asked_for_one()
    {
        var world = await CarrierStoreAsync();

        var points = await world.Shopper.GetJsonAsync<List<PickupPointView>>(
            $"/api/storefront/checkout/pickup-points/{MapCarrier.MethodCode}");

        Assert.Empty(points);
        Assert.False(_carrier.AskedForAList);
    }

    [Fact]
    public async Task A_point_the_carrier_does_not_know_stops_the_order()
    {
        var world = await CarrierStoreAsync();
        _carrier.Knows = false;

        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", CarrierCheckout("Z-BOX-9999"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // Only the code comes from the browser; what the point is called and where it stands is the carrier's to
    // say, so a forged name never reaches the order.
    [Fact]
    public async Task The_point_is_kept_as_the_carrier_describes_it()
    {
        var world = await CarrierStoreAsync();

        using var placed = await world.Shopper.PostAsync("/api/storefront/checkout", CarrierCheckout("Z-BOX-0042"));
        var order = await world.Shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var stored = await StoredAsync(world, order.Number);

        Assert.Equal("Z-BOX-0042", stored.PickupPointCode);
        Assert.Equal("Z-BOX Hlavní nádraží", stored.PickupPointName);
        Assert.Equal(("Wilsonova 8", "Praha", "CZ"), (stored.PickupPointAddress!.Line1, stored.PickupPointAddress.City, stored.PickupPointAddress.Country));
    }

    [Fact]
    public async Task A_parcel_the_carrier_will_not_take_is_neither_offered_nor_accepted()
    {
        var world = await CarrierStoreAsync(quantity: 2);
        _carrier.RefusesOver = 1000;

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", CarrierCheckout("Z-BOX-0042"));

        Assert.DoesNotContain(MapCarrier.MethodCode, methods.ShippingMethods.Select(method => method.Code));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // The store's own limit, which the carrier never hears about because the method never gets that far.
    [Fact]
    public async Task A_method_the_store_limited_by_weight_drops_out_of_the_offer()
    {
        var world = await CarrierStoreAsync(quantity: 2, maxWeightGrams: 1000);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        using var refused = await world.Shopper.PostAsync("/api/storefront/checkout", CarrierCheckout("Z-BOX-0042"));

        Assert.DoesNotContain(MapCarrier.MethodCode, methods.ShippingMethods.Select(method => method.Code));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.False(_carrier.AskedToCarry);
    }

    // An unknown weight is not a light one: a method that promises to stay under a limit cannot promise it
    // about a parcel nobody has weighed.
    [Fact]
    public async Task A_method_with_a_limit_will_not_take_a_cart_nobody_has_weighed()
    {
        var world = await CarrierStoreAsync(maxWeightGrams: 5000, weightGrams: null);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");

        Assert.DoesNotContain(MapCarrier.MethodCode, methods.ShippingMethods.Select(method => method.Code));
    }

    // Where it is going is not known while the shopper is still choosing, so this one is caught at checkout.
    [Fact]
    public async Task A_method_that_does_not_serve_the_country_is_refused_when_the_address_says_so()
    {
        var world = await CarrierStoreAsync(countries: ["CZ"]);

        var methods = await world.Shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");
        using var elsewhere = await world.Shopper.PostAsync("/api/storefront/checkout", CarrierCheckout("Z-BOX-0042"));
        using var athome = await world.Shopper.PostAsync("/api/storefront/checkout", CarrierCheckout("Z-BOX-0042", country: "CZ"));

        Assert.Contains(MapCarrier.MethodCode, methods.ShippingMethods.Select(method => method.Code));
        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.Created, athome.StatusCode);
    }

    private static object CarrierCheckout(string pickupPointCode, string country = "IE") => new
    {
        Email = "buyer@example.test",
        Phone = "+420 123 456 789",
        BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Dublin", PostalCode = "D01 AB12", Country = country },
        ShippingAddress = (object?)null,
        PaymentMethodCode = "bank-transfer",
        ShippingMethodCode = MapCarrier.MethodCode,
        PickupPointCode = pickupPointCode,
    };

    private Task<Order> StoredAsync(CarrierWorld world, string number) =>
        _factory.QueryAsync(world.Furniture.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == number, CancellationToken));

    private async Task<CarrierWorld> CarrierStoreAsync(
        int quantity = 1,
        int? maxWeightGrams = null,
        IReadOnlyList<string>? countries = null,
        int? weightGrams = 850)
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        using var admin = await TestUsers.LoginAsync(_withCarrier, await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId));

        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods",
            new
            {
                Name = "Z-BOX",
                ProviderKey = MapCarrier.ProviderKey,
                Price = 59m,
                VatRate = 21m,
                IsActive = true,
                RequiresPickupPoint = true,
                MaxWeightGrams = maxWeightGrams,
                Countries = countries,
            },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);

        var productId = await admin.CreateProductAsync();

        using var weighed = await admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}",
            new { Ean = "8594000000013", WeightGrams = weightGrams },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, weighed.StatusCode);

        var storeProductId = await admin.ListProductAsync(furniture.Store.StoreId, productId, "Parcel Chair", 100m);
        await admin.StockAsync(productId, 10);

        var shopper = new StorefrontApi(_withCarrier, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        return new CarrierWorld(furniture, shopper);
    }

    private sealed record CarrierWorld(FurnitureStore Furniture, StorefrontApi Shopper);

    private sealed record MethodsView(List<ShippingMethodView> ShippingMethods);

    private sealed record ShippingMethodView(string Code, string Name, decimal Price, bool RequiresPickupPoint);

    private sealed record PickupPointView(string Code, string Name, string City);
}

// A carrier whose points are chosen in its own map: it has no list to hand over, it is the only authority on
// whether a code is one of its boxes, and it can refuse a parcel the store was willing to send.
internal sealed class MapCarrier : IShippingProvider
{
    public const string ProviderKey = "map-carrier";
    public const string MethodCode = "z-box";

    public string Key => ProviderKey;

    public PickupPointChoice PickupPoints => PickupPointChoice.InTheCarriersMap;

    public bool Knows { get; set; } = true;

    public int? RefusesOver { get; set; }

    public bool AskedForAList { get; private set; }

    public bool AskedToCarry { get; private set; }

    public Task<IReadOnlyList<PickupPoint>> FindPickupPointsAsync(CancellationToken cancellationToken)
    {
        AskedForAList = true;

        return Task.FromResult<IReadOnlyList<PickupPoint>>([]);
    }

    public Task<PickupPoint?> FindPickupPointAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Knows
            ? new PickupPoint(code, "Z-BOX Hlavní nádraží", "Wilsonova 8", "Praha", "110 00", "CZ")
            : null);

    public Task<bool> CanCarryAsync(Parcel parcel, CancellationToken cancellationToken)
    {
        AskedToCarry = true;

        return Task.FromResult(RefusesOver is not { } limit || parcel.WeightGrams <= limit);
    }

    public Task<ShipmentDetails> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ShipmentDetails("Packeta", request.TrackingNumber ?? "Z1234", TrackingUrl: null));
}
