using System.Net;

namespace ShopForge.IntegrationTests.Catalog;

internal sealed record FurnitureStore(TestStore Store, TestStore OtherStore, HttpClient Admin, Guid ChairsCategoryId, Dictionary<string, Guid> Products)
{
    public static async Task<FurnitureStore> CreateAsync(ShopForgeApiFactory factory)
    {
        var (store, otherStore) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        var storeId = store.StoreId;

        var material = await admin.CreateAttributeAsync(storeId, new { Name = "Material", Type = "select", IsFilterable = true, IsVisibleOnProductPage = true, Options = new[] { "Oak", "Walnut", "Beech" } });
        var width = await admin.CreateAttributeAsync(storeId, new { Name = "Width", Type = "decimal", Unit = "cm", IsFilterable = true, IsVisibleOnProductPage = true });
        await admin.CreateAttributeAsync(storeId, new { Name = "Foldable", Type = "boolean", IsFilterable = true, IsVisibleOnProductPage = true });
        await admin.CreateAttributeAsync(storeId, new { Name = "Colors", Type = "multiSelect", IsFilterable = true, IsVisibleOnProductPage = true, Options = new[] { "Black", "White", "Natural" } });
        await admin.CreateAttributeAsync(storeId, new { Name = "Seats", Type = "integer", IsFilterable = true, IsVisibleOnProductPage = false });
        await admin.CreateAttributeAsync(storeId, new { Name = "Released", Type = "date", IsFilterable = true, IsVisibleOnProductPage = true });
        await admin.CreateAttributeAsync(storeId, new { Name = "Care", Type = "text", IsVisibleOnProductPage = true });

        var chairs = await admin.CreateCategoryAsync(storeId, "Chairs");
        using var assigned = await admin.AssignCategoryAttributesAsync(storeId, chairs, material, width);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var products = new Dictionary<string, Guid>
        {
            ["oak-chair"] = await AddAsync(admin, storeId, "Oak Chair", 100m, chairs, new { material = "oak", width = 45, foldable = false, colors = new[] { "natural" }, seats = 1, released = "2024-03-01", care = "Oil once a year" }),
            ["walnut-chair"] = await AddAsync(admin, storeId, "Walnut Chair", 200m, chairs, new { material = "walnut", width = 50, foldable = true, colors = new[] { "black", "natural" }, seats = 1, released = "2025-01-15" }),
            ["beech-stool"] = await AddAsync(admin, storeId, "Beech Stool", 50m, chairs, new { material = "beech", width = 30.5, foldable = false, colors = new[] { "white" }, seats = 1, released = "2023-06-30" }),
            ["oak-bench"] = await AddAsync(admin, storeId, "Oak Bench", 300m, categoryId: null, new { material = "oak", width = 120, foldable = false, colors = new[] { "natural" }, seats = 3 }),
        };

        return new FurnitureStore(store, otherStore, admin, chairs, products);
    }

    private static async Task<Guid> AddAsync(HttpClient admin, Guid storeId, string name, decimal price, Guid? categoryId, object values)
    {
        var storeProductId = await admin.ListProductAsync(storeId, await admin.CreateProductAsync(), name, price);

        using var setValues = await admin.SetAttributesAsync(storeId, storeProductId, values);
        Assert.Equal(HttpStatusCode.OK, setValues.StatusCode);

        if (categoryId is not null)
        {
            using var assign = await admin.AssignCategoriesAsync(storeId, storeProductId, categoryId.Value);
            Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        }

        return storeProductId;
    }
}
