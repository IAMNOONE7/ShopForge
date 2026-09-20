using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Catalog;

internal static class AdminCatalogApi
{
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    public static string UniqueSku() => $"SKU-{Guid.NewGuid():N}"[..20];

    public static async Task<Guid> CreateProductAsync(this HttpClient admin, string? sku = null)
    {
        using var response = await admin.PostAsJsonAsync("/api/admin/products", new { Sku = sku ?? UniqueSku(), Ean = "8594000000001", WeightGrams = 850 });
        return await IdFromAsync(response);
    }

    public static async Task<Guid> ListProductAsync(
        this HttpClient admin, Guid storeId, Guid productId, string name, decimal price, bool isVisible = true, string? slug = null)
    {
        using var response = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/products",
            new { ProductId = productId, Name = name, Slug = slug, Description = $"{name} description", Price = price, VatRate = 21m, IsVisible = isVisible, SortOrder = 0 });
        return await IdFromAsync(response);
    }

    public static async Task<HttpResponseMessage> SetStockAsync(this HttpClient admin, Guid productId, int quantity) =>
        await admin.PutAsJsonAsync($"/api/admin/stock/{productId}", new { Quantity = quantity }, TestContext.Current.CancellationToken);

    public static async Task StockAsync(this HttpClient admin, Guid productId, int quantity)
    {
        using var response = await admin.SetStockAsync(productId, quantity);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task<Guid> CreateCategoryAsync(this HttpClient admin, Guid storeId, string name)
    {
        using var response = await admin.PostAsJsonAsync($"/api/admin/stores/{storeId}/categories", new { Name = name, SortOrder = 0 });
        return await IdFromAsync(response);
    }

    public static async Task<Guid> CreateAttributeAsync(this HttpClient admin, Guid storeId, object attribute)
    {
        using var response = await admin.PostAsJsonAsync($"/api/admin/stores/{storeId}/attributes", attribute);
        return await IdFromAsync(response);
    }

    public static Task<HttpResponseMessage> SetAttributesAsync(this HttpClient admin, Guid storeId, Guid storeProductId, object values) =>
        admin.PutAsJsonAsync($"/api/admin/stores/{storeId}/products/{storeProductId}/attributes", new { Values = values });

    public static Task<HttpResponseMessage> AssignCategoryAttributesAsync(this HttpClient admin, Guid storeId, Guid categoryId, params Guid[] attributeIds) =>
        admin.PutAsJsonAsync($"/api/admin/stores/{storeId}/categories/{categoryId}/attributes", new { AttributeIds = attributeIds });

    public static Task<HttpResponseMessage> AssignCategoriesAsync(this HttpClient admin, Guid storeId, Guid storeProductId, params Guid[] categoryIds) =>
        admin.PutAsJsonAsync($"/api/admin/stores/{storeId}/products/{storeProductId}/categories", new { CategoryIds = categoryIds });

    public static async Task<HttpResponseMessage> UploadImageAsync(this HttpClient admin, Guid productId, byte[] bytes, string fileName = "photo.png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        form.Add(new StringContent("Front view"), "altText");

        return await admin.PostAsync($"/api/admin/products/{productId}/images", form);
    }

    private static async Task<Guid> IdFromAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Created>();
        return created!.Id;
    }

    private sealed record Created(Guid Id);
}
