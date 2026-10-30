using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Development;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Search;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests.Search;

// The development data's Czech tool shop (`ToolShopCatalogue`), set up through the admin API as a merchant would: its
// category tree, its attributes and the categories that offer them, and one import of its workbook (D-225, D-227). Built
// once for the tests that read it, which change nothing in it.
public sealed class ToolShop(ShopForgeApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private TestStore? _store;

    internal TestStore Store => _store ?? throw new InvalidOperationException("The tool shop has not been set up yet.");

    internal HttpClient Admin { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _store = await TestStores.CreateInCurrencyAsync(factory.Services, "CZK", "cs-CZ");
        Admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, Store.TenantId));
        var categories = new Dictionary<string, Guid>();

        foreach (var (category, index) in ToolShopCatalogue.Categories.Select((category, index) => (category, index)))
        {
            using var created = await Admin.PostAsJsonAsync(
                $"/api/admin/stores/{Store.StoreId}/categories",
                new { category.Name, category.Slug, SortOrder = index, ParentId = category.ParentSlug is { } parent ? categories[parent] : (Guid?)null, SearchKeywords = category.Keywords },
                CancellationToken);
            categories[category.Slug] = await IdOfAsync(created);
        }

        var offered = new Dictionary<string, List<Guid>>();

        foreach (var attribute in ToolShopCatalogue.Attributes)
        {
            var id = await Admin.CreateAttributeAsync(Store.StoreId, new
            {
                attribute.Code,
                attribute.Name,
                attribute.Type,
                attribute.Unit,
                IsFilterable = attribute.Filterable,
                IsVisibleOnProductPage = true,
                IsSearchable = attribute.Searchable,
                attribute.Options,
            });

            foreach (var category in attribute.FilterIn)
            {
                (offered.TryGetValue(category, out var ids) ? ids : offered[category] = []).Add(id);
            }
        }

        foreach (var (category, ids) in offered)
        {
            using var assigned = await Admin.AssignCategoryAttributesAsync(Store.StoreId, categories[category], [.. ids]);
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        }

        using var imported = await Admin.ImportAsync(Store.StoreId, ImportFiles.Upload(DevelopmentToolShop.ProductsWorkbook(), "tool-shop.xlsx"));
        var report = await imported.Content.ReadFromJsonAsync<ImportReport>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal((ToolShopCatalogue.Products.Count, 0, 0), (report!.Created, report.Invalid, report.Failed));
    }

    public ValueTask DisposeAsync()
    {
        Admin?.Dispose();

        return ValueTask.CompletedTask;
    }

    // The codes of what a search finds, in the order it answers.
    internal async Task<IReadOnlyList<string>> SearchAsync(string typed, string paging = "&pageSize=50")
    {
        using var shopper = new StorefrontApi(factory, Store);
        var page = await shopper.GetJsonAsync<Page>($"/api/storefront/products?q={Uri.EscapeDataString(typed)}{paging}");

        return [.. page.Items.Select(item => SkuOf(item.Slug))];
    }

    // Why each listing a search finds answers it, by its code: the tier the ranking gives it (D-225).
    internal async Task<IReadOnlyDictionary<string, int>> TiersAsync(string typed)
    {
        await using var scope = TestStores.CreateScope(factory.Services, Store);
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<DbContext>();
        var settings = await services.GetRequiredService<ICurrentStoreSettings>().GetAsync(CancellationToken);
        var hits = await SearchTerms.Of(dbContext, services.GetRequiredService<IStoreContext>(), settings, typed)!.Ranked().ToListAsync(CancellationToken);
        var ids = hits.Select(hit => hit.Id).ToList();
        var slugs = await dbContext.Set<StoreProduct>()
            .Where(listing => ids.Contains(listing.Id))
            .ToDictionaryAsync(listing => listing.Id, listing => listing.Slug, CancellationToken);

        return hits.ToDictionary(hit => SkuOf(slugs[hit.Id]), hit => hit.Tier);
    }

    private static string SkuOf(string slug) =>
        ToolShopCatalogue.Products.Single(product => product.Slug == slug).Sku;

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(CancellationToken))!.Id;
    }

    private sealed record Created(Guid Id);

    private sealed record ImportReport(int Created, int Invalid, int Failed);

    private sealed record Page(List<Item> Items);

    private sealed record Item(string Slug);
}
