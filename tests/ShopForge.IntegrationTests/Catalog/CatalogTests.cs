using System.Net;
using System.Net.Http.Json;
using ShopForge.Access.Domain;

namespace ShopForge.IntegrationTests.Catalog;

public sealed class CatalogTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_physical_product_is_sold_by_two_stores_with_their_own_name_and_price()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, storeA.TenantId));
        var productId = await admin.CreateProductAsync();

        await admin.ListProductAsync(storeA.StoreId, productId, "Oak Kitchen Cutting Board", 499m);
        await admin.ListProductAsync(storeB.StoreId, productId, "Premium Oak Cutting Board", 529m);

        var productsA = await GetStorefrontProductsAsync(storeA);
        var productsB = await GetStorefrontProductsAsync(storeB);
        Assert.Equal(("Oak Kitchen Cutting Board", 499m), (productsA.Items.Single().Name, productsA.Items.Single().Price));
        Assert.Equal(("Premium Oak Cutting Board", 529m), (productsB.Items.Single().Name, productsB.Items.Single().Price));
    }

    [Fact]
    public async Task Hidden_products_are_not_shown_in_the_storefront()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        await admin.ListProductAsync(store.StoreId, await admin.CreateProductAsync(), "Visible board", 10m);
        await admin.ListProductAsync(store.StoreId, await admin.CreateProductAsync(), "Hidden board", 10m, isVisible: false);

        var products = await GetStorefrontProductsAsync(store);
        using var hiddenDetail = await factory.CreateClient().GetAsync($"http://{store.HostName}/api/storefront/products/hidden-board", CancellationToken);

        Assert.Equal(["Visible board"], products.Items.Select(item => item.Name));
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetail.StatusCode);
    }

    [Fact]
    public async Task Category_page_lists_only_products_assigned_to_the_category()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        var boards = await admin.CreateCategoryAsync(store.StoreId, "Cutting Boards");
        var board = await admin.ListProductAsync(store.StoreId, await admin.CreateProductAsync(), "Walnut board", 20m);
        await admin.ListProductAsync(store.StoreId, await admin.CreateProductAsync(), "Oak chair", 90m);

        using var assign = await admin.AssignCategoriesAsync(store.StoreId, board, boards);
        var inCategory = await GetStorefrontProductsAsync(store, "?category=cutting-boards");
        using var unknownCategory = await factory.CreateClient().GetAsync(
            $"http://{store.HostName}/api/storefront/products?category=nope", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Equal(["Walnut board"], inCategory.Items.Select(item => item.Name));
        Assert.Equal(HttpStatusCode.NotFound, unknownCategory.StatusCode);
    }

    [Fact]
    public async Task Categories_of_another_store_cannot_be_assigned()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, storeA.TenantId));
        var categoryOfB = await admin.CreateCategoryAsync(storeB.StoreId, "Chairs");
        var productOfA = await admin.ListProductAsync(storeA.StoreId, await admin.CreateProductAsync(), "Board", 5m);

        using var response = await admin.AssignCategoriesAsync(storeA.StoreId, productOfA, categoryOfB);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Products_and_stores_of_another_tenant_are_not_accessible()
    {
        var (ownStore, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (foreignStore, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var foreignAdmin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, foreignStore.TenantId));
        var foreignProduct = await foreignAdmin.CreateProductAsync();
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, ownStore.TenantId));

        var products = await admin.GetFromJsonAsync<List<IdOnly>>("/api/admin/products", CancellationToken);
        using var listForeignProduct = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{ownStore.StoreId}/products",
            new { ProductId = foreignProduct, Name = "Stolen", Price = 1m, IsVisible = true },
            CancellationToken);
        using var foreignStoreProducts = await admin.GetAsync($"/api/admin/stores/{foreignStore.StoreId}/products", CancellationToken);

        Assert.DoesNotContain(products!, product => product.Id == foreignProduct);
        Assert.Equal(HttpStatusCode.BadRequest, listForeignProduct.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignStoreProducts.StatusCode);
    }

    [Fact]
    public async Task Catalog_changes_require_a_catalog_role()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var support = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId, TenantRole.Support));

        using var read = await support.GetAsync($"/api/admin/stores/{store.StoreId}/products", CancellationToken);
        using var write = await support.PostAsJsonAsync("/api/admin/products", new { Sku = AdminCatalogApi.UniqueSku() }, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Duplicates_and_invalid_input_are_rejected()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        var sku = AdminCatalogApi.UniqueSku();
        var productId = await admin.CreateProductAsync(sku);
        await admin.ListProductAsync(store.StoreId, productId, "Board", 5m, slug: "board");

        using var duplicateSku = await admin.PostAsJsonAsync("/api/admin/products", new { Sku = sku.ToLowerInvariant() }, CancellationToken);
        using var duplicateSlug = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/products",
            new { ProductId = await admin.CreateProductAsync(), Name = "Board", Slug = "board", Price = 5m, IsVisible = true },
            CancellationToken);
        using var negativePrice = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/products",
            new { ProductId = await admin.CreateProductAsync(), Name = "Cheap", Price = -1m, IsVisible = true },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, duplicateSku.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateSlug.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negativePrice.StatusCode);
    }

    [Fact]
    public async Task Uploaded_images_are_served_only_by_stores_that_sell_the_product()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, storeA.TenantId));
        var productId = await admin.CreateProductAsync();
        await admin.ListProductAsync(storeA.StoreId, productId, "Pictured board", 15m, slug: "pictured-board");
        using var upload = await admin.UploadImageAsync(productId, AdminCatalogApi.PngBytes);
        using var storefront = factory.CreateClient();

        var detail = await storefront.GetFromJsonAsync<ProductDetail>(
            $"http://{storeA.HostName}/api/storefront/products/pictured-board", CancellationToken);
        var imageUrl = detail!.Images.Single().Url;
        using var image = await storefront.GetAsync($"http://{storeA.HostName}{imageUrl}", CancellationToken);
        using var imageViaOtherStore = await storefront.GetAsync($"http://{storeB.HostName}{imageUrl}", CancellationToken);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AdminCatalogApi.PngBytes, await image.Content.ReadAsByteArrayAsync(CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, imageViaOtherStore.StatusCode);
    }

    [Fact]
    public async Task Files_that_are_not_images_are_rejected_regardless_of_their_declared_type()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

        using var response = await admin.UploadImageAsync(await admin.CreateProductAsync(), "<script>alert(1)</script>"u8.ToArray(), "evil.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<ProductPage> GetStorefrontProductsAsync(TestStore store, string query = "")
    {
        using var storefront = factory.CreateClient();
        return (await storefront.GetFromJsonAsync<ProductPage>($"http://{store.HostName}/api/storefront/products{query}", CancellationToken))!;
    }

    private sealed record IdOnly(Guid Id);

    private sealed record ProductPage(List<ProductSummary> Items, int TotalCount);

    private sealed record ProductSummary(Guid Id, string Name, decimal Price, string? ImageUrl);

    private sealed record ProductDetail(Guid Id, string Name, List<ProductImage> Images);

    private sealed record ProductImage(string Url, string? AltText);
}
