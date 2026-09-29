using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Health;

// What the application does when something it depends on is not there. Everything else in the suite runs against
// working infrastructure; these are the paths nobody exercises until the day it matters (D-132). The database
// losing its connection is next door in HealthEndpointTests, and a payment provider that will not answer is in
// StripePaymentTests — this is the storage half.
public sealed class OutageTests(ShopForgeApiFactory factory)
{
    // Azurite's own well-known development credentials, pointed at a port nothing answers on.
    private const string UnreachableStorage =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
        + "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        + "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_blob_outage_fails_readiness_but_not_liveness()
    {
        await using var withoutStorage = WithoutStorage();
        using var client = withoutStorage.CreateClient();

        using var readiness = await client.GetAsync("/health/ready", CancellationToken);
        using var liveness = await client.GetAsync("/health/live", CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    // The file is written before the row, so a store that cannot reach its storage ends up with neither rather
    // than with a product whose image is a broken link for ever.
    [Fact]
    public async Task An_upload_that_cannot_reach_storage_leaves_no_image_behind()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];
        var user = await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId);

        await using var withoutStorage = WithoutStorage();
        using var admin = await TestUsers.LoginAsync(withoutStorage, user);

        using var upload = await admin.UploadImageAsync(productId, AdminCatalogApi.PngBytes);
        var images = await ImagesAsync(furniture, productId);

        Assert.Equal(HttpStatusCode.InternalServerError, upload.StatusCode);
        Assert.Empty(images);
    }

    // One dependency being down is not the shop being down: everything that does not touch storage keeps selling.
    [Fact]
    public async Task A_blob_outage_does_not_stop_the_shop_selling()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await using var withoutStorage = WithoutStorage();
        using var shopper = new StorefrontApi(withoutStorage, furniture.Store);

        using var products = await shopper.GetAsync("/api/storefront/products");
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
    }

    private WebApplicationFactory<Program> WithoutStorage() =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:FileStorage", UnreachableStorage));

    private Task<List<ProductImage>> ImagesAsync(FurnitureStore furniture, Guid productId) =>
        factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Set<Product>()
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .SelectMany(product => product.Images)
            .ToListAsync(CancellationToken));
}
