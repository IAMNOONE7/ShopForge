using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Payments;

namespace ShopForge.IntegrationTests.Catalog;

public sealed class AttributeIsolationTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Attribute_data_is_visible_only_to_its_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var dataA = await CreateAsync(storeA);
        var dataB = await CreateAsync(storeB);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        var definitions = await dbContext.Set<AttributeDefinition>().Where(item => item.Id == dataA.MaterialId || item.Id == dataB.MaterialId).Select(item => item.Id).ToListAsync(CancellationToken);
        var options = await dbContext.Set<AttributeOption>().Where(item => item.AttributeDefinitionId == dataA.MaterialId || item.AttributeDefinitionId == dataB.MaterialId).Select(item => item.AttributeDefinitionId).Distinct().ToListAsync(CancellationToken);
        var values = await dbContext.Set<ProductAttributeValue>().Where(item => item.StoreProductId == dataA.StoreProductId || item.StoreProductId == dataB.StoreProductId).Select(item => item.StoreProductId).Distinct().ToListAsync(CancellationToken);
        var categoryAttributes = await dbContext.Set<CategoryAttribute>().Where(item => item.AttributeDefinitionId == dataA.MaterialId || item.AttributeDefinitionId == dataB.MaterialId).Select(item => item.AttributeDefinitionId).ToListAsync(CancellationToken);

        Assert.Equal([dataA.MaterialId], definitions);
        Assert.Equal([dataA.MaterialId], options);
        Assert.Equal([dataA.StoreProductId], values);
        Assert.Equal([dataA.MaterialId], categoryAttributes);
    }

    [Fact]
    public async Task Database_rejects_an_option_used_with_another_attribute()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var data = await CreateAsync(store);

        var exception = await SaveInvalidValueAsync(store, new ProductAttributeValue(
            store.StoreId, data.StoreProductId, data.FinishId, new AttributeValue { OptionIds = [data.OakOptionId] }));

        Assert.Contains("fk_product_attribute_values_attribute_options", exception.InnerException?.Message);
    }

    [Fact]
    public async Task Database_rejects_a_value_with_more_than_one_typed_column()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var data = await CreateAsync(store);

        var exception = await SaveInvalidValueAsync(store, new ProductAttributeValue(
            store.StoreId, data.StoreProductId, data.WidthId, new AttributeValue { Decimal = 45m, Integer = 45 }));

        Assert.Contains("ck_product_attribute_values_single_value", exception.InnerException?.Message);
    }

    [Fact]
    public async Task Database_rejects_a_second_value_for_a_single_value_attribute()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var data = await CreateAsync(store);

        var exception = await SaveInvalidValueAsync(store, new ProductAttributeValue(
            store.StoreId, data.StoreProductId, data.WidthId, new AttributeValue { Decimal = 99m }));

        Assert.Contains("ix_product_attribute_values_single", exception.InnerException?.Message);
    }

    private async Task<DbUpdateException> SaveInvalidValueAsync(TestStore store, ProductAttributeValue value)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Add(value);

        return await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken));
    }

    private async Task<(Guid MaterialId, Guid FinishId, Guid WidthId, Guid OakOptionId, Guid StoreProductId)> CreateAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var settings = new AttributeSettings(Unit: null, IsFilterable: true, IsVisibleOnProductPage: true, SortOrder: 0);

        var material = new AttributeDefinition(store.StoreId, "material", "Material", AttributeType.Select, settings);
        var oak = material.AddOption("Oak", "oak");
        var finish = new AttributeDefinition(store.StoreId, "finish", "Finish", AttributeType.Select, settings);
        finish.AddOption("Matte", "matte");
        var width = new AttributeDefinition(store.StoreId, "width", "Width", AttributeType.Decimal, settings);
        var category = new Category(store.StoreId, "Chairs", $"chairs-{Guid.NewGuid():N}", 0);
        category.AssignAttributes([material]);

        var product = new Product(store.TenantId, $"SKU-{Guid.NewGuid():N}", ean: null, weightGrams: null);
        var storeProduct = new StoreProduct(store.StoreId, product, new StoreProductDetails("Chair", $"chair-{Guid.NewGuid():N}", null, 10m, 21m, true, 0), Currency.Of("EUR"));
        storeProduct.ReplaceAttributeValues([(material, new AttributeValue { OptionIds = [oak.Id] }), (width, new AttributeValue { Decimal = 45m })]);

        dbContext.AddRange(material, finish, width, category, product, storeProduct);
        await dbContext.SaveChangesAsync(CancellationToken);

        return (material.Id, finish.Id, width.Id, oak.Id, storeProduct.Id);
    }
}
