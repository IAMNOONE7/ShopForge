using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Shipping;

// The store's own van, priced by the parcel: a delivery that needs no account, so nothing but the method's own terms
// decides whether the checkout offers it (D-218, D-219). It takes 10 kg and 80 × 60 × 60 cm to Czechia, and costs 8 a
// parcel up to 3 kg and 12 up to 10 kg. The furniture store's chairs are 850 g in a 450 × 400 × 90 mm box, so eight
// fill a parcel to its share of volume and ten make two parcels: 7100 g and 2000 g, 20 in all.
internal static class OwnVan
{
    public const string MethodName = "Own van by the parcel";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static async Task<string> CreateAsync(ShopForgeApiFactory factory, FurnitureStore furniture) =>
        (await CreateWithServiceAsync(factory, furniture)).Code;

    public static async Task<(string Code, string ServiceKey)> CreateWithServiceAsync(ShopForgeApiFactory factory, FurnitureStore furniture)
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
            new { Name = MethodName, ProviderKey = "manual", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = false, Countries = new[] { "CZ" }, Pricing = "perParcel", CarrierService = key },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var code = (await created.Content.ReadFromJsonAsync<CreatedMethod>(CancellationToken))!.Code;

        await PriceAsync(furniture, code, ("own", 3_000, 8m), ("own", 10_000, 12m));

        return (code, key);
    }

    // The operator takes the van off the road: the service is switched off, and every method sending by it with it.
    public static async Task SwitchOffAsync(ShopForgeApiFactory factory, string serviceKey)
    {
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        using var switchedOff = await operatorClient.PutAsJsonAsync(
            $"/api/platform/carrier-services/{serviceKey}",
            new
            {
                Name = "Own van, Czechia",
                IsActive = false,
                MaxWeightGrams = 10_000,
                Largest = new { LengthMm = 800, WidthMm = 600, HeightMm = 600 },
                MaxSumOfSidesMm = 2_000,
                Smallest = new { LengthMm = 50, WidthMm = 50, HeightMm = 10 },
                LargestVolumeMm3 = 200_000_000L,
                Source = "Our own van, measured.",
                SourceDate = new DateOnly(2026, 10, 25),
            },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, switchedOff.StatusCode);
    }

    public static async Task PriceAsync(FurnitureStore furniture, string code, params (string Source, int UpToGrams, decimal Price)[] bands)
    {
        using var priced = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/{code}/rates",
            new { Source = bands[0].Source, Bands = bands.Select(band => new { band.UpToGrams, band.Price }) },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);
    }

    // A checkout to a Czech address, which is where the van goes.
    public static object Checkout(string shippingMethodCode, decimal? expectedShippingPrice, string country = "CZ") => new
    {
        Email = "buyer@example.test",
        Phone = "+420 777 123 456",
        BillingAddress = new { FullName = "Eva Kupující", Line1 = "Wilsonova 8", Line2 = (string?)null, City = "Praha", PostalCode = "110 00", Country = country },
        ShippingAddress = (object?)null,
        PaymentMethodCode = "bank-transfer",
        ShippingMethodCode = shippingMethodCode,
        PickupPointCode = (string?)null,
        ExpectedShippingPrice = expectedShippingPrice,
    };

    private sealed record CreatedMethod(string Code);
}
