using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Domain;
using ShopForge.Infrastructure.Persistence;

namespace ShopForge.IntegrationTests.Catalog;

public sealed class CatalogIsolationTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Store_owned_catalog_data_is_visible_only_to_its_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var catalogA = await CreateCatalogAsync(storeA);
        var catalogB = await CreateCatalogAsync(storeB);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        var storeProducts = await dbContext.Set<StoreProduct>()
            .Where(item => item.Id == catalogA.StoreProductId || item.Id == catalogB.StoreProductId).Select(item => item.Id).ToListAsync(CancellationToken);
        var categories = await dbContext.Set<Category>()
            .Where(item => item.Id == catalogA.CategoryId || item.Id == catalogB.CategoryId).Select(item => item.Id).ToListAsync(CancellationToken);
        var assignments = await dbContext.Set<ProductCategory>()
            .Where(item => item.CategoryId == catalogA.CategoryId || item.CategoryId == catalogB.CategoryId).Select(item => item.CategoryId).ToListAsync(CancellationToken);

        Assert.Equal([catalogA.StoreProductId], storeProducts);
        Assert.Equal([catalogA.CategoryId], categories);
        Assert.Equal([catalogA.CategoryId], assignments);
    }

    [Fact]
    public async Task Tenant_products_are_shared_by_its_stores_but_not_across_tenants()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (foreignStore, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var catalogA = await CreateCatalogAsync(storeA);

        Assert.True(await ProductVisibleAsync(storeB, catalogA.ProductId));
        Assert.False(await ProductVisibleAsync(foreignStore, catalogA.ProductId));
    }

    [Fact]
    public async Task Database_rejects_linking_a_product_to_a_category_of_another_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var catalogA = await CreateCatalogAsync(storeA);
        var catalogB = await CreateCatalogAsync(storeB);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Passes the application-level guard (the row claims store A); only the composite foreign key can stop it.
        dbContext.Add(new ProductCategory(storeA.StoreId, catalogA.StoreProductId, catalogB.CategoryId));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken));
        Assert.Contains("fk_product_categories_categories_store_id_category_id", exception.InnerException?.Message);
    }

    [Fact]
    public async Task Store_cannot_write_catalog_data_of_another_store()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var catalogB = await CreateCatalogAsync(storeB);
        await using var scope = TestStores.CreateScope(factory.Services, storeA);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Add(new Category(storeB.StoreId, "Injected", "injected", 0));
        dbContext.Add(new ProductCategory(storeB.StoreId, catalogB.StoreProductId, catalogB.CategoryId));

        await Assert.ThrowsAsync<TenancyViolationException>(() => dbContext.SaveChangesAsync(CancellationToken));
    }

    private async Task<bool> ProductVisibleAsync(TestStore store, Guid productId)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);
        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Product>().AnyAsync(product => product.Id == productId, CancellationToken);
    }

    private async Task<(Guid ProductId, Guid StoreProductId, Guid CategoryId)> CreateCatalogAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(factory.Services, store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        var product = new Product(store.TenantId, $"SKU-{Guid.NewGuid():N}", ean: null, weightGrams: null);
        var category = new Category(store.StoreId, "Boards", $"boards-{Guid.NewGuid():N}", 0);
        var storeProduct = new StoreProduct(store.StoreId, product, new StoreProductDetails("Board", $"board-{Guid.NewGuid():N}", null, 10m, true, 0));
        storeProduct.AssignCategories([category]);

        dbContext.AddRange(product, category, storeProduct);
        await dbContext.SaveChangesAsync(CancellationToken);

        return (product.Id, storeProduct.Id, category.Id);
    }
}
