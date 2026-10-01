using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Catalog;

// A store selling one thing in two sizes: the smallest world in which a form of a product can be told apart
// from the product (D-134, D-135).
internal sealed record ShirtStore(FurnitureStore Furniture, Guid ProductId, Guid Listing, string Slug, Guid Small, Guid Large)
{
    public const string Name = "Linen Shirt";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static async Task<ShirtStore> CreateAsync(ShopForgeApiFactory factory)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var admin = furniture.Admin;
        var sku = $"SHIRT-{Guid.NewGuid():N}"[..20];

        var productId = await admin.CreateProductAsync(sku);
        var small = await admin.DefaultVariantIdAsync(productId);

        using var named = await admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}/options",
            new { Names = new[] { "Size" }, Values = new Dictionary<Guid, string[]> { [small] = ["S"] } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);

        using var added = await admin.PostAsJsonAsync(
            $"/api/admin/products/{productId}/variants",
            new { Sku = sku + "-L", Ean = (string?)null, WeightGrams = (int?)null, OptionValues = new[] { "L" } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var large = (await added.Content.ReadFromJsonAsync<CreatedVariant>(CancellationToken))!.Id;

        var slug = $"shirt-{Guid.NewGuid():N}"[..20];
        var listing = await admin.ListProductAsync(furniture.Store.StoreId, productId, Name, 60m, slug: slug);

        return new ShirtStore(furniture, productId, listing, slug, small, large);
    }

    public async Task StockAsync(int small, int large)
    {
        await Furniture.Admin.VariantStockAsync(Small, small);
        await Furniture.Admin.VariantStockAsync(Large, large);
    }

    private sealed record CreatedVariant(Guid Id);
}
