using System.Net;
using System.Net.Http.Json;
using ShopForge.Catalog.Domain;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Catalog;

// Categories nest, a parent sells what its children sell, and both a shopper and a crawler can walk from the
// top of the shop down to any product (D-146, D-172).
public sealed class CategoryTreeTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Furniture > Chairs (the fixture's three) > Dining, with one chair of its own in Dining.
    private async Task<Nested> NestedShopAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var storeId = furniture.Store.StoreId;
        var top = await furniture.Admin.CreateCategoryAsync(storeId, "Furniture");
        var dining = await furniture.Admin.CreateCategoryAsync(storeId, "Dining Chairs", furniture.ChairsCategoryId);

        await ReparentAsync(furniture, furniture.ChairsCategoryId, top);

        var productId = await furniture.Admin.CreateProductAsync(FurnitureStore.SkuOf("dining-chair"));
        var listing = await furniture.Admin.ListProductAsync(storeId, productId, "Dining Chair", 400m, slug: "dining-chair");
        await furniture.Admin.StockAsync(productId, 5);
        using var assigned = await furniture.Admin.AssignCategoriesAsync(storeId, listing, dining);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        return new Nested(furniture, top, dining, listing);
    }

    // The behaviour change this slice carries: a parent page sells what everything beneath it sells (D-146).
    [Fact]
    public async Task A_parent_category_sells_what_its_children_sell()
    {
        var shop = await NestedShopAsync();

        var top = await PageAsync(shop.Furniture, "furniture");
        var chairs = await PageAsync(shop.Furniture, "chairs");
        var dining = await PageAsync(shop.Furniture, "dining-chairs");

        // Four chairs beneath Furniture; the uncategorised bench is in none of them.
        Assert.Equal(4, top.TotalCount);
        Assert.Equal(4, chairs.TotalCount);
        Assert.Equal(1, dining.TotalCount);
        Assert.Contains("dining-chair", top.Items.Select(item => item.Slug));
        Assert.DoesNotContain("oak-bench", top.Items.Select(item => item.Slug));
    }

    // A product in a parent and in its child is one product, not two. The count and the page have to agree,
    // because a shopper reading "4 products" and counting three is reading a bug.
    [Fact]
    public async Task A_product_listed_in_a_parent_and_its_child_is_named_once()
    {
        var shop = await NestedShopAsync();
        using var both = await shop.Furniture.Admin.AssignCategoriesAsync(
            shop.Furniture.Store.StoreId, shop.Listing, shop.Dining, shop.Top);
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);

        var top = await PageAsync(shop.Furniture, "furniture");

        Assert.Single(top.Items, item => item.Slug == "dining-chair");
        Assert.Equal(4, top.TotalCount);
        Assert.Equal(top.Items.Count, top.TotalCount);
    }

    // Counts, facets, sorting and paging all read the same set, so a facet that says three must be able to
    // show three.
    [Fact]
    public async Task A_facet_count_and_a_page_of_results_agree_on_the_parents_page()
    {
        var shop = await NestedShopAsync();

        var page = await PageAsync(shop.Furniture, "furniture");
        var oak = page.Filters.SingleOrDefault(filter => filter.Code == "material")?.Options?.Single(option => option.Code == "oak");
        var filtered = await PageAsync(shop.Furniture, "furniture", "&f.material=oak");

        Assert.NotNull(oak);
        Assert.Equal(oak.Count, filtered.TotalCount);
        Assert.Equal(oak.Count, filtered.Items.Count);
    }

    [Fact]
    public async Task Sorting_and_paging_run_over_the_whole_set_beneath_a_parent()
    {
        var shop = await NestedShopAsync();

        var cheapestFirst = await PageAsync(shop.Furniture, "furniture", "&sort=price&pageSize=2");
        var second = await PageAsync(shop.Furniture, "furniture", "&sort=price&pageSize=2&page=2");

        Assert.Equal(4, cheapestFirst.TotalCount);
        Assert.Equal(["beech-stool", "oak-chair"], cheapestFirst.Items.Select(item => item.Slug));
        Assert.Equal(["walnut-chair", "dining-chair"], second.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task A_category_page_says_what_is_above_it_and_what_sits_under_it()
    {
        var shop = await NestedShopAsync();

        var chairs = await PageAsync(shop.Furniture, "chairs");
        var top = await PageAsync(shop.Furniture, "furniture");

        Assert.Equal(["Furniture", "Chairs"], chairs.Path.Select(crumb => crumb.Name));
        Assert.Equal(["Dining Chairs"], chairs.Children.Select(child => child.Name));
        Assert.Equal(["Furniture"], top.Path.Select(crumb => crumb.Name));
        Assert.Equal(["Chairs"], top.Children.Select(child => child.Name));
        Assert.Empty((await PageAsync(shop.Furniture, "dining-chairs")).Children);
    }

    // A crawler on a product page can walk back up to the top of the shop without another request.
    [Fact]
    public async Task A_product_carries_the_whole_path_above_each_category_it_is_in()
    {
        var shop = await NestedShopAsync();
        using var shopper = new StorefrontApi(factory, shop.Furniture.Store);

        var product = await shopper.GetJsonAsync<ProductDetail>("/api/storefront/products/dining-chair");
        var dining = product.Categories.Single();

        Assert.Equal("Dining Chairs", dining.Name);
        Assert.Equal("chairs", dining.ParentSlug);
        Assert.Equal(["Furniture", "Chairs", "Dining Chairs"], dining.Path.Select(crumb => crumb.Name));
    }

    [Fact]
    public async Task The_shops_category_list_is_flat_in_tree_order_and_says_who_each_sits_under()
    {
        var shop = await NestedShopAsync();
        using var shopper = new StorefrontApi(factory, shop.Furniture.Store);

        var categories = await shopper.GetJsonAsync<List<StorefrontCategory>>("/api/storefront/categories");

        Assert.Equal(["Furniture", "Chairs", "Dining Chairs"], categories.Select(category => category.Name));
        Assert.Null(categories[0].ParentSlug);
        Assert.Equal("furniture", categories[1].ParentSlug);
        Assert.Equal("chairs", categories[2].ParentSlug);
        Assert.Equal(["Furniture", "Chairs", "Dining Chairs"], categories[2].Path.Select(crumb => crumb.Name));
    }

    [Fact]
    public async Task A_category_can_be_moved_to_the_top_and_takes_its_children_with_it()
    {
        var shop = await NestedShopAsync();

        await ReparentAsync(shop.Furniture, shop.Furniture.ChairsCategoryId, null);
        var chairs = await PageAsync(shop.Furniture, "chairs");
        var top = await PageAsync(shop.Furniture, "furniture");

        Assert.Equal(["Chairs"], chairs.Path.Select(crumb => crumb.Name));
        Assert.Equal(["Dining Chairs"], chairs.Children.Select(child => child.Name));
        Assert.Equal(4, chairs.TotalCount);
        Assert.Equal(0, top.TotalCount);
    }

    [Fact]
    public async Task A_category_cannot_be_moved_beneath_one_of_its_own()
    {
        var shop = await NestedShopAsync();

        using var response = await MoveAsync(shop.Furniture, shop.Top, shop.Dining);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("beneath one of its own", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_category_cannot_be_its_own_parent()
    {
        var shop = await NestedShopAsync();

        using var response = await MoveAsync(shop.Furniture, shop.Top, shop.Top);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("its own parent", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_parent_that_does_not_exist_is_refused_on_the_way_in_and_on_the_way_past()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var storeId = furniture.Store.StoreId;

        using var created = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/categories",
            new { Name = "Orphan", SortOrder = 0, ParentId = Guid.NewGuid() },
            CancellationToken);
        using var moved = await MoveAsync(furniture, furniture.ChairsCategoryId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
    }

    // One store's category is no parent for another's, even inside one tenant.
    [Fact]
    public async Task A_category_of_another_store_is_not_a_parent()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var theirs = await furniture.Admin.CreateCategoryAsync(furniture.OtherStore.StoreId, "Theirs");

        using var response = await MoveAsync(furniture, furniture.ChairsCategoryId, theirs);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nesting_stops_at_the_depth_limit()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var storeId = furniture.Store.StoreId;
        var deepest = furniture.ChairsCategoryId;

        for (var level = 2; level <= CategoryTree.MaxDepth; level++)
        {
            deepest = await furniture.Admin.CreateCategoryAsync(storeId, $"Level {level}", deepest);
        }

        using var tooDeep = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId}/categories",
            new { Name = "One too far", SortOrder = 0, ParentId = deepest },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
        Assert.Contains($"{CategoryTree.MaxDepth} levels", await tooDeep.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // A branch that fits where it is may not fit where it is going, which neither end can tell on its own.
    [Fact]
    public async Task A_move_that_would_push_a_branch_past_the_limit_is_refused()
    {
        var shop = await NestedShopAsync();
        var storeId = shop.Furniture.Store.StoreId;
        var deepest = await shop.Furniture.Admin.CreateCategoryAsync(storeId, "Elsewhere");

        for (var level = 2; level < CategoryTree.MaxDepth; level++)
        {
            deepest = await shop.Furniture.Admin.CreateCategoryAsync(storeId, $"Deep {level}", deepest);
        }

        // Furniture is three levels tall; the category it is being moved under is four deep.
        using var response = await MoveAsync(shop.Furniture, shop.Top, deepest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"{CategoryTree.MaxDepth} levels", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_admin_lists_categories_with_their_parent_and_in_tree_order()
    {
        var shop = await NestedShopAsync();

        var categories = await shop.Furniture.Admin.GetFromJsonAsync<List<AdminCategory>>(
            $"/api/admin/stores/{shop.Furniture.Store.StoreId}/categories", CancellationToken);

        Assert.NotNull(categories);
        Assert.Equal(["Furniture", "Chairs", "Dining Chairs"], categories.Select(category => category.Name));
        Assert.Null(categories[0].ParentId);
        Assert.Equal(shop.Top, categories[1].ParentId);
        Assert.Equal(shop.Furniture.ChairsCategoryId, categories[2].ParentId);
    }

    // Every category a shop has, however deep, is still offered to a crawler.
    [Fact]
    public async Task A_nested_category_is_in_the_sitemap()
    {
        var shop = await NestedShopAsync();
        using var shopper = new StorefrontApi(factory, shop.Furniture.Store);

        var sitemap = await shopper.GetStringAsync("/api/storefront/sitemap.xml");

        Assert.Contains("/c/dining-chairs", sitemap, StringComparison.Ordinal);
        Assert.Contains("/c/furniture", sitemap, StringComparison.Ordinal);
    }

    private async Task<ProductPage> PageAsync(FurnitureStore furniture, string categorySlug, string extra = "")
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);

        return await shopper.GetJsonAsync<ProductPage>($"/api/storefront/products?category={categorySlug}{extra}");
    }

    private static async Task ReparentAsync(FurnitureStore furniture, Guid categoryId, Guid? parentId)
    {
        using var response = await MoveAsync(furniture, categoryId, parentId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> MoveAsync(FurnitureStore furniture, Guid categoryId, Guid? parentId)
    {
        var categories = await furniture.Admin.GetFromJsonAsync<List<AdminCategory>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories", CancellationToken);
        var category = categories!.Single(candidate => candidate.Id == categoryId);


        return await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{categoryId}",
            new { category.Name, category.Slug, category.SortOrder, ParentId = parentId },
            CancellationToken);
    }

    private sealed record Nested(FurnitureStore Furniture, Guid Top, Guid Dining, Guid Listing);

    private sealed record AdminCategory(Guid Id, string Name, string Slug, int SortOrder, Guid? ParentId);

    private sealed record StorefrontCategory(string Name, string Slug, string? ParentSlug, List<StorefrontCategory> Path);

    private sealed record ProductPage(
        List<PagedItem> Items,
        int TotalCount,
        List<Facet> Filters,
        List<StorefrontCategory> Path,
        List<StorefrontCategory> Children);

    private sealed record PagedItem(string Slug, string Name, decimal Price);

    private sealed record Facet(string Code, List<FacetOption>? Options);

    private sealed record FacetOption(string Code, int Count);

    private sealed record ProductDetail(string Slug, List<StorefrontCategory> Categories);
}
