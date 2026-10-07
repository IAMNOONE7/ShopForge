using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Admin;

namespace ShopForge.IntegrationTests.Admin;

// Every admin list answers the same way: a page, a total, and what the merchant was looking for (D-179).
public sealed class AdminListTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_list_says_how_many_there_are_as_well_as_which_ones_it_is_giving()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await ListingsAsync(furniture, "?pageSize=2");

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task The_last_page_says_there_is_no_more()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var last = await ListingsAsync(furniture, "?pageSize=3&page=2");

        Assert.Single(last.Items);
        Assert.Equal(4, last.TotalCount);
        Assert.False(last.HasMore);
    }

    // The boundary the "is there more" turns on: a list that ends exactly where a page ends has no next page,
    // and offering one sends a merchant to an empty screen.
    [Fact]
    public async Task A_list_that_ends_exactly_where_a_page_ends_says_there_is_no_more()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var second = await ListingsAsync(furniture, "?pageSize=2&page=2");
        var first = await ListingsAsync(furniture, "?pageSize=2&page=1");

        Assert.Equal(2, second.Items.Count);
        Assert.False(second.HasMore);
        Assert.True(first.HasMore);
    }

    // One letter matches most of a catalogue and is not a search; the list comes back whole instead.
    [Fact]
    public async Task One_letter_is_not_a_search()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var oneLetter = await ListingsAsync(furniture, "?q=o");
        var two = await ListingsAsync(furniture, "?q=oa");

        Assert.Equal(4, oneLetter.TotalCount);
        Assert.Equal(2, two.TotalCount);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_rather_than_a_refusal()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var past = await ListingsAsync(furniture, "?pageSize=2&page=99");

        Assert.Empty(past.Items);
        Assert.Equal(4, past.TotalCount);
        Assert.False(past.HasMore);
    }

    // Nobody asks for a hundred thousand rows by accident twice.
    [Fact]
    public async Task A_page_bigger_than_the_limit_is_cut_to_it_rather_than_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await ListingsAsync(furniture, "?pageSize=100000");

        Assert.Equal(AdminListQuery.MaxPageSize, page.PageSize);
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task Nonsense_paging_falls_back_to_the_first_page()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await ListingsAsync(furniture, "?page=-3&pageSize=0");

        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
    }

    // The merchant looking for a row they know exists: by name, by address, or by the code on the box.
    [Fact]
    public async Task A_listing_is_found_by_name_by_slug_and_by_code()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        // The slug is given a word of its own: a code is built from the slug, so any slug that is also in the
        // code would be found by the code and prove nothing about looking at the slug.
        await ReslugAsync(furniture, "beech-stool", "sgabello");

        var byName = await ListingsAsync(furniture, "?q=walnut");
        var bySlug = await ListingsAsync(furniture, "?q=sgabello");
        var byCode = await ListingsAsync(furniture, $"?q={FurnitureStore.SkuOf("oak-bench")}");

        Assert.Equal(["Walnut Chair"], byName.Items.Select(item => item.Name));
        Assert.Equal(["Beech Stool"], bySlug.Items.Select(item => item.Name));
        Assert.Equal(["Oak Bench"], byCode.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Looking_for_something_is_case_insensitive_and_counts_only_what_matches()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var shouted = await ListingsAsync(furniture, "?q=WALNUT");

        Assert.Single(shouted.Items);
        Assert.Equal(1, shouted.TotalCount);
    }

    // A per cent sign is a character somebody typed, not an instruction to match everything.
    [Fact]
    public async Task A_wildcard_in_what_was_typed_is_a_character_rather_than_a_wildcard()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        // "%oak" taken literally finds nothing; taken as a wildcard it would find every oak thing there is.
        var literal = await ListingsAsync(furniture, "?q=%25oak");
        var plain = await ListingsAsync(furniture, "?q=oak");

        Assert.Empty(literal.Items);
        Assert.Equal(0, literal.TotalCount);
        Assert.Equal(2, plain.TotalCount);
    }

    [Fact]
    public async Task A_list_can_be_ordered_by_something_other_than_the_shops_own_order()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var cheapest = await ListingsAsync(furniture, "?sort=price");
        var dearest = await ListingsAsync(furniture, "?sort=-price");

        Assert.Equal(["Beech Stool", "Oak Chair", "Walnut Chair", "Oak Bench"], cheapest.Items.Select(item => item.Name));
        Assert.Equal(["Oak Bench", "Walnut Chair", "Oak Chair", "Beech Stool"], dearest.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task An_order_nobody_understands_falls_back_to_the_shops_own()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var nonsense = await ListingsAsync(furniture, "?sort=whatever");
        var plain = await ListingsAsync(furniture, "");

        Assert.Equal(plain.Items.Select(item => item.Name), nonsense.Items.Select(item => item.Name));
    }

    // Orders used to stop at the latest two hundred, which is a wall rather than a page.
    [Fact]
    public async Task Orders_are_paged_and_searchable_by_number_and_by_who_placed_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var first = await PlaceOrderAsync(furniture);
        await PlaceOrderAsync(furniture);

        var all = await OrdersAsync(furniture, "");
        var firstPage = await OrdersAsync(furniture, "?pageSize=1");
        var byNumber = await OrdersAsync(furniture, $"?q={first}");
        var byEmail = await OrdersAsync(furniture, "?q=buyer@example.test");

        Assert.Equal(2, all.TotalCount);
        Assert.Single(firstPage.Items);
        Assert.True(firstPage.HasMore);
        Assert.Equal([first], byNumber.Items.Select(item => item.Number));
        Assert.Equal(2, byEmail.TotalCount);
    }

    [Fact]
    public async Task Orders_come_newest_first_unless_asked_otherwise()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var first = await PlaceOrderAsync(furniture);
        var second = await PlaceOrderAsync(furniture);

        var newestFirst = await OrdersAsync(furniture, "");
        var oldestFirst = await OrdersAsync(furniture, "?sort=placed");

        Assert.Equal([second, first], newestFirst.Items.Select(item => item.Number));
        Assert.Equal([first, second], oldestFirst.Items.Select(item => item.Number));
    }

    // Stock movements stopped at fifty, which for anything that sells is most of the story missing.
    [Fact]
    public async Task Stock_movements_are_paged()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var variantId = furniture.VariantIds["oak-chair"];

        for (var quantity = 1; quantity <= 3; quantity++)
        {
            await furniture.Admin.VariantStockAsync(variantId, FurnitureStore.StockPerProduct + quantity);
        }

        var all = await MovementsAsync(furniture, variantId, "");
        var one = await MovementsAsync(furniture, variantId, "?pageSize=1");

        Assert.True(all.TotalCount >= 3, $"expected at least three movements, saw {all.TotalCount}");
        Assert.Single(one.Items);
        Assert.True(one.HasMore);
    }

    // A page of one shop's list is never a page of another's.
    [Fact]
    public async Task A_list_is_the_stores_own_however_it_is_paged()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var theirs = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.OtherStore.StoreId}/categories",
            new { Name = "Theirs", SortOrder = 0 },
            CancellationToken);
        theirs.Dispose();

        var ours = await ListingsAsync(furniture, "?pageSize=200");

        Assert.Equal(4, ours.TotalCount);
        Assert.All(ours.Items, item => Assert.Contains(item.Name, new[] { "Oak Chair", "Walnut Chair", "Beech Stool", "Oak Bench" }));
    }

    // The address only: the product's code is its own and does not follow.
    private async Task ReslugAsync(FurnitureStore furniture, string slug, string to)
    {
        var listing = (await ListingsAsync(furniture, "?pageSize=200")).Items.Single(row => row.Slug == slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { listing.Name, Slug = to, Description = (string?)null, listing.Price, VatRate = 21m, IsVisible = true, listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private Task<AdminListResponse<ListingRow>> ListingsAsync(FurnitureStore furniture, string query) =>
        furniture.Admin.AdminPageAsync<ListingRow>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products{query}", CancellationToken);

    private Task<AdminListResponse<OrderRow>> OrdersAsync(FurnitureStore furniture, string query) =>
        furniture.Admin.AdminPageAsync<OrderRow>(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders{query}", CancellationToken);

    private Task<AdminListResponse<MovementRow>> MovementsAsync(FurnitureStore furniture, Guid variantId, string query) =>
        furniture.Admin.AdminPageAsync<MovementRow>($"/api/admin/stock/{variantId}/movements{query}", CancellationToken);

    private async Task<string> PlaceOrderAsync(FurnitureStore furniture)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: "buyer@example.test"));

        return (await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created)).Number;
    }

    private sealed record ListingRow(Guid Id, string Name, string Slug, decimal Price, int SortOrder);

    private sealed record OrderRow(string Number, decimal GrandTotal);

    private sealed record MovementRow(DateTimeOffset OccurredAt, int Quantity);
}
