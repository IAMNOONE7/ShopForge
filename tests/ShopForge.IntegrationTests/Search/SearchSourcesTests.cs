using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Search;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Maintenance;

namespace ShopForge.IntegrationTests.Search;

// A product is found by what people call it, not only by what its name says (D-226): the merchant's keywords, its
// brand, barcode and part number, the categories it is filed under and those above them, and the options of an
// attribute marked searchable. Every path that changes one of those words makes the change findable at once, in every
// store that sells the product.
public sealed class SearchSourcesTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // A keyword finds a listing, and a listing whose name says the word comes before it.
    [Fact]
    public async Task A_listing_is_found_by_its_keywords_after_one_whose_name_says_so()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var saved = await SaveListingAsync(furniture, "oak-chair", keywords: "armchair, seat");
        await SaveListingAsync(furniture, "beech-stool", name: "Armchair Stool");

        var found = await SearchAsync(furniture.Store, "armchair");

        Assert.Equal("armchair, seat", saved.SearchKeywords);
        Assert.Equal(["beech-stool", "oak-chair"], found);
    }

    [Fact]
    public async Task A_listing_created_with_keywords_is_found_by_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = await furniture.Admin.CreateProductAsync();

        using var listed = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products",
            new { ProductId = product, Name = "Ottoman", Description = "A footstool.", Price = 80m, VatRate = 21m, IsVisible = true, SortOrder = 0, SearchKeywords = "pouffe" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, listed.StatusCode);
        Assert.Equal(["ottoman"], await SearchAsync(furniture.Store, "pouffe"));
    }

    // A screen that does not know the keywords yet changes nothing by saving; sending them empty takes them away.
    [Fact]
    public async Task Keywords_left_out_stay_and_keywords_sent_empty_go()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SaveListingAsync(furniture, "oak-chair", keywords: "armchair");

        await SaveListingAsync(furniture, "oak-chair", name: "Oak Chair");
        var kept = await SearchAsync(furniture.Store, "armchair");
        await SaveListingAsync(furniture, "oak-chair", keywords: "");
        var cleared = await SearchAsync(furniture.Store, "armchair");

        Assert.Equal(["oak-chair"], kept);
        Assert.Empty(cleared);
    }

    [Fact]
    public async Task Keywords_longer_than_five_hundred_characters_are_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var listing = await ListingAsync(furniture, "oak-chair");

        using var listingRefused = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { listing.Name, listing.Slug, listing.Description, listing.Price, listing.VatRate, listing.IsVisible, listing.SortOrder, SearchKeywords = new string('a', 501) },
            CancellationToken);
        using var categoryRefused = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories", new { Name = "Lounge", SortOrder = 0, SearchKeywords = new string('a', 501) }, CancellationToken);

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (listingRefused.StatusCode, categoryRefused.StatusCode));
        Assert.Contains("searchKeywords", await listingRefused.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // A listing is found by the name and the keywords of each category it is filed under and of those above them.
    [Fact]
    public async Task A_listing_is_found_by_its_categories_and_those_above_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var lounge = await CategoryAsync(furniture, "Lounge", keywords: "lobby");
        var recliners = await CategoryAsync(furniture, "Recliners", parent: lounge);
        await FileAsync(furniture, "oak-chair", recliners);

        var byOwn = await SearchAsync(furniture.Store, "recliners");
        var byParent = await SearchAsync(furniture.Store, "lounge");
        var byParentsKeyword = await SearchAsync(furniture.Store, "lobby");

        Assert.Equal(["oak-chair"], byOwn);
        Assert.Equal(["oak-chair"], byParent);
        Assert.Equal(["oak-chair"], byParentsKeyword);
    }

    [Fact]
    public async Task A_renamed_category_changes_what_finds_its_listings_at_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var lounge = await CategoryAsync(furniture, "Lounge");
        var recliners = await CategoryAsync(furniture, "Recliners", parent: lounge);
        await FileAsync(furniture, "oak-chair", recliners);

        await UpdateCategoryAsync(furniture, lounge, "Parlour", parent: null);
        var byNew = await SearchAsync(furniture.Store, "parlour");
        var byOld = await SearchAsync(furniture.Store, "lounge");

        Assert.Equal(["oak-chair"], byNew);
        Assert.Empty(byOld);
    }

    [Fact]
    public async Task A_moved_category_takes_the_words_of_where_it_now_sits()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var lounge = await CategoryAsync(furniture, "Lounge");
        var garden = await CategoryAsync(furniture, "Garden");
        var recliners = await CategoryAsync(furniture, "Recliners", parent: lounge);
        await FileAsync(furniture, "oak-chair", recliners);

        await UpdateCategoryAsync(furniture, recliners, "Recliners", parent: garden);
        var byNewParent = await SearchAsync(furniture.Store, "garden");
        var byOldParent = await SearchAsync(furniture.Store, "lounge");

        Assert.Equal(["oak-chair"], byNewParent);
        Assert.Empty(byOldParent);
    }

    [Fact]
    public async Task An_archived_category_finds_nothing_until_it_is_restored()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var recliners = await CategoryAsync(furniture, "Recliners");
        await FileAsync(furniture, "oak-chair", recliners);

        using var archived = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/categories/{recliners}/archive", null, CancellationToken);
        var whileArchived = await SearchAsync(furniture.Store, "recliners");
        using var restored = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/categories/{recliners}/restore", null, CancellationToken);
        var afterRestoring = await SearchAsync(furniture.Store, "recliners");

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (archived.StatusCode, restored.StatusCode));
        Assert.Empty(whileArchived);
        Assert.Equal(["oak-chair"], afterRestoring);
    }

    // A retired parent's words leave the categories beneath it, while the child, still in use, keeps its own.
    [Fact]
    public async Task An_archived_parent_s_name_leaves_its_children_and_theirs_stays()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var lounge = await CategoryAsync(furniture, "Lounge");
        var recliners = await CategoryAsync(furniture, "Recliners", parent: lounge);
        await FileAsync(furniture, "oak-chair", recliners);

        using var archived = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/categories/{lounge}/archive", null, CancellationToken);
        var byParent = await SearchAsync(furniture.Store, "lounge");
        var byChild = await SearchAsync(furniture.Store, "recliners");

        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.Empty(byParent);
        Assert.Equal(["oak-chair"], byChild);
    }

    // A retired category takes what is filed only under it out of its parents' words too, as it takes it off their
    // pages: on the storefront a parent shows what the live categories beneath it sell (D-146).
    [Fact]
    public async Task An_archived_child_takes_what_is_filed_under_it_out_of_its_parent_s_words()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var lounge = await CategoryAsync(furniture, "Lounge");
        var recliners = await CategoryAsync(furniture, "Recliners", parent: lounge);
        await FileAsync(furniture, "oak-chair", recliners);

        using var archived = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/categories/{recliners}/archive", null, CancellationToken);
        var byParent = await SearchAsync(furniture.Store, "lounge");

        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.Empty(byParent);
    }

    [Fact]
    public async Task A_category_s_keywords_find_what_is_filed_under_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var before = await SearchAsync(furniture.Store, "seating");

        var saved = await UpdateCategoryAsync(furniture, furniture.ChairsCategoryId, "Chairs", parent: null, keywords: "seating");
        var after = await SearchAsync(furniture.Store, "seating");

        Assert.Empty(before);
        Assert.Equal("seating", saved.SearchKeywords);
        Assert.Equal(["beech-stool", "oak-chair", "walnut-chair"], after.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_listing_filed_elsewhere_is_found_by_where_it_now_is()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var recliners = await CategoryAsync(furniture, "Recliners");
        var rockers = await CategoryAsync(furniture, "Rockers");
        await FileAsync(furniture, "oak-chair", recliners);

        await FileAsync(furniture, "oak-chair", rockers);
        var byNew = await SearchAsync(furniture.Store, "rockers");
        var byOld = await SearchAsync(furniture.Store, "recliners");

        Assert.Equal(["oak-chair"], byNew);
        Assert.Empty(byOld);
    }

    [Fact]
    public async Task Listings_filed_in_bulk_are_found_by_it_at_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var outdoor = await CategoryAsync(furniture, "Outdoor");
        Guid[] chosen = [furniture.Products["oak-bench"], furniture.Products["beech-stool"]];

        using var added = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/bulk/categories", new { StoreProductIds = chosen, AddCategoryIds = new[] { outdoor } }, CancellationToken);
        var filed = await SearchAsync(furniture.Store, "outdoor");
        using var removed = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/bulk/categories", new { StoreProductIds = chosen, RemoveCategoryIds = new[] { outdoor } }, CancellationToken);
        var unfiled = await SearchAsync(furniture.Store, "outdoor");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (added.StatusCode, removed.StatusCode));
        Assert.Equal(["beech-stool", "oak-bench"], filed.Order(StringComparer.Ordinal));
        Assert.Empty(unfiled);
    }

    // An option's name is a word once its attribute is searched, and a renamed option is the new word at once.
    [Fact]
    public async Task An_option_is_found_once_its_attribute_is_searched_and_by_its_new_name_once_renamed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var oak = material.Options.Single(option => option.Code == "oak").Id;

        using var firstName = await RenameOptionAsync(furniture, material, oak, "Solid timber");
        var whileNotSearched = await SearchAsync(furniture.Store, "timber");
        using var searched = await SaveAttributeAsync(furniture, material, searchable: true);
        var onceSearched = await SearchAsync(furniture.Store, "timber");
        using var secondName = await RenameOptionAsync(furniture, material, oak, "English heartwood");
        var byNewName = await SearchAsync(furniture.Store, "heartwood");
        var byOldName = await SearchAsync(furniture.Store, "timber");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (firstName.StatusCode, searched.StatusCode, secondName.StatusCode));
        Assert.Empty(whileNotSearched);
        Assert.Equal(["oak-bench", "oak-chair"], onceSearched.Order(StringComparer.Ordinal));
        Assert.Equal(["oak-bench", "oak-chair"], byNewName.Order(StringComparer.Ordinal));
        Assert.Empty(byOldName);
    }

    [Fact]
    public async Task A_number_is_still_filtered_rather_than_searched()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await SaveAttributeAsync(furniture, await AttributeAsync(furniture, "Width"), searchable: true);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // The product is the company's and sold by two of its stores, which read in different languages; changing its brand
    // or its part number for the company is findable at once in both.
    [Fact]
    public async Task A_brand_and_a_part_number_set_for_the_company_are_found_at_once_in_every_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SellInOtherStoreAsync(furniture, "oak-chair", "cs-CZ");

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{furniture.ProductIds["oak-chair"]}",
            new { Brand = "Dubex", PartNumber = "DX-100", Ean = "2000000003009", WeightGrams = 850, LengthMm = 450, WidthMm = 400, HeightMm = 90 },
            CancellationToken);
        var here = await SearchAsync(furniture.Store, "dubex");
        var there = await SearchAsync(furniture.OtherStore, "dubex");
        var byPartNumber = await SearchAsync(furniture.OtherStore, "DX-100");
        var byBarcode = await SearchAsync(furniture.OtherStore, "2000000003009");

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(["oak-chair"], here);
        Assert.Equal(["dubova-zidle"], there);
        Assert.Equal(["dubova-zidle"], byPartNumber);
        Assert.Equal(["dubova-zidle"], byBarcode);
    }

    // I10: a SKU renamed for the company used to wait for the nightly pass in every store but none.
    [Fact]
    public async Task A_renamed_code_is_found_at_once_in_every_store_and_the_old_one_no_longer()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SellInOtherStoreAsync(furniture, "oak-chair", "en-IE");

        using var renamed = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{furniture.ProductIds["oak-chair"]}/variants/{furniture.VariantIds["oak-chair"]}",
            new { Sku = "RENAMED-OAK-7", Ean = "8594000000006", WeightGrams = 850, LengthMm = 450, WidthMm = 400, HeightMm = 90, OptionValues = Array.Empty<string>() },
            CancellationToken);
        var here = await SearchAsync(furniture.Store, "RENAMED-OAK-7");
        var there = await SearchAsync(furniture.OtherStore, "RENAMED-OAK-7");
        var oldThere = await SearchAsync(furniture.OtherStore, FurnitureStore.SkuOf("oak-chair"));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(["oak-chair"], here);
        Assert.Equal(["dubova-zidle"], there);
        Assert.Empty(oldThere);
    }

    // A form added to the company's product is findable by its code at once in every store, and one removed no longer.
    [Fact]
    public async Task A_form_added_or_removed_for_the_company_changes_what_finds_it_at_once_in_every_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SellInOtherStoreAsync(furniture, "oak-chair", "en-IE");
        var product = furniture.ProductIds["oak-chair"];

        using var axes = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{product}/options",
            new { Names = new[] { "Height" }, Values = new Dictionary<Guid, string[]> { [furniture.VariantIds["oak-chair"]] = ["Low"] } },
            CancellationToken);
        using var added = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/products/{product}/variants",
            new { Sku = "TALL-OAK-9", WeightGrams = 900, LengthMm = 450, WidthMm = 400, HeightMm = 90, OptionValues = new[] { "Tall" } },
            CancellationToken);
        var tall = await added.Content.ReadFromJsonAsync<VariantView>(CancellationToken);
        var whileSold = await SearchAsync(furniture.OtherStore, "TALL-OAK-9");
        using var removed = await furniture.Admin.DeleteAsync($"/api/admin/products/{product}/variants/{tall!.Id}", CancellationToken);
        var afterwards = await SearchAsync(furniture.OtherStore, "TALL-OAK-9");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent), (axes.StatusCode, added.StatusCode, removed.StatusCode));
        Assert.Equal(["dubova-zidle"], whileSold);
        Assert.Empty(afterwards);
    }

    // A file can name a category the store does not have yet; the import makes it, and its name finds what is in it.
    [Fact]
    public async Task A_category_a_file_makes_finds_what_the_file_filed_in_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(furniture, ["sku", "categories"], [FurnitureStore.SkuOf("oak-bench"), "Hammocks"]);

        Assert.Equal((1, 0), (report.Updated, report.Invalid));
        Assert.Equal(["oak-bench"], await SearchAsync(furniture.Store, "hammocks"));
    }

    // The nightly pass writes the words of a category that no path wrote, such as one a development seed made, before
    // it reads the catalogue again.
    [Fact]
    public async Task The_nightly_pass_writes_the_words_of_a_category_nothing_wrote()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        // The store as a seed would leave it: categories without their words, and listings indexed without them.
        await using (var scope = TestStores.CreateScope(factory.Services, furniture.Store))
        {
            await scope.ServiceProvider.GetRequiredService<DbContext>().Database.ExecuteSqlAsync(
                $"UPDATE catalog.categories SET search_text = NULL WHERE store_id = {furniture.Store.StoreId}", CancellationToken);
            await scope.ServiceProvider.GetRequiredService<SearchIndex>().RefreshStoreAsync(CancellationToken);
        }

        var beforeThePass = await SearchAsync(furniture.Store, "chairs");

        await using (var scope = TestStores.CreateScope(factory.Services, furniture.Store))
        {
            await scope.ServiceProvider.GetServices<IStoreMaintenance>()
                .Single(maintenance => maintenance.Name == "Search index")
                .RunAsync(CancellationToken);
        }

        var afterThePass = await SearchAsync(furniture.Store, "chairs");

        Assert.Equal(["oak-chair", "walnut-chair"], beforeThePass.Order(StringComparer.Ordinal));
        Assert.Equal(["beech-stool", "oak-chair", "walnut-chair"], afterThePass.Order(StringComparer.Ordinal));
    }

    // A file one store imports changes the company's product, so the other store that sells it finds the change too.
    [Fact]
    public async Task A_brand_a_file_brings_in_is_found_at_once_in_the_other_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SellInOtherStoreAsync(furniture, "oak-chair", "en-IE");

        var report = await ImportAsync(furniture, ["sku", "brand"], [FurnitureStore.SkuOf("oak-chair"), "Dubex"]);
        var there = await SearchAsync(furniture.OtherStore, "dubex");

        Assert.Equal((1, 0), (report.Updated, report.Invalid));
        Assert.Equal(["dubova-zidle"], there);
    }

    // An empty cell leaves the keywords alone, as an empty cell does in every column; a column the file lacks does too.
    [Fact]
    public async Task Keywords_a_file_brings_in_are_found_and_an_empty_cell_or_no_column_leaves_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var sku = FurnitureStore.SkuOf("oak-chair");

        var brought = await ImportAsync(furniture, ["sku", "search_keywords"], [sku, "rocking, armchair"]);
        var emptyCell = await ImportAsync(furniture, ["sku", "search_keywords", "name"], [sku, null, "Oak Armchair"]);
        var noColumn = await ImportAsync(furniture, ["sku", "name"], [sku, "Oak Chair"]);
        var found = await SearchAsync(furniture.Store, "rocking");

        Assert.Equal((1, 0), (brought.Updated, brought.Invalid));
        Assert.Equal((1, 1), (emptyCell.Updated, noColumn.Updated));
        Assert.Equal(["oak-chair"], found);
        Assert.Equal("rocking, armchair", (await ListingAsync(furniture, "oak-chair")).SearchKeywords);
    }

    [Fact]
    public async Task Keywords_too_long_are_refused_on_their_row_and_the_rest_of_the_file_imports()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(
            furniture,
            ["sku", "search_keywords"],
            [FurnitureStore.SkuOf("oak-chair"), new string('a', 501)],
            [FurnitureStore.SkuOf("walnut-chair"), "dark wood"]);

        Assert.Equal((1, 1), (report.Updated, report.Invalid));
        Assert.Contains(report.Issues, issue => issue.Column == "search_keywords");
        Assert.Equal(["walnut-chair"], await SearchAsync(furniture.Store, "dark"));
    }

    private async Task<List<string>> SearchAsync(TestStore store, string typed)
    {
        using var shopper = new StorefrontApi(factory, store);
        var page = await shopper.GetJsonAsync<ProductPage>($"/api/storefront/products?q={Uri.EscapeDataString(typed)}");

        return [.. page.Items.Select(item => item.Slug)];
    }

    private async Task<ListingView> SaveListingAsync(FurnitureStore furniture, string slug, string? name = null, string? keywords = null)
    {
        var listing = await ListingAsync(furniture, slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { Name = name ?? listing.Name, listing.Slug, listing.Description, listing.Price, listing.VatRate, listing.IsVisible, listing.SortOrder, SearchKeywords = keywords },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        return (await saved.Content.ReadFromJsonAsync<ListingView>(CancellationToken))!;
    }

    private async Task<ListingView> ListingAsync(FurnitureStore furniture, string slug)
    {
        var listings = await furniture.Admin.AdminListAsync<ListingView>($"/api/admin/stores/{furniture.Store.StoreId}/products", CancellationToken);

        return listings!.Single(candidate => candidate.Slug == slug);
    }

    private async Task<Guid> CategoryAsync(FurnitureStore furniture, string name, Guid? parent = null, string? keywords = null)
    {
        using var created = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories", new { Name = name, SortOrder = 0, ParentId = parent, SearchKeywords = keywords }, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await created.Content.ReadFromJsonAsync<CategoryView>(CancellationToken))!.Id;
    }

    private async Task<CategoryView> UpdateCategoryAsync(FurnitureStore furniture, Guid category, string name, Guid? parent, string? keywords = null)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{category}",
            new { Name = name, SortOrder = 0, ParentId = parent, SearchKeywords = keywords },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        return (await saved.Content.ReadFromJsonAsync<CategoryView>(CancellationToken))!;
    }

    private async Task FileAsync(FurnitureStore furniture, string slug, Guid category)
    {
        using var filed = await furniture.Admin.AssignCategoriesAsync(furniture.Store.StoreId, furniture.Products[slug], category);

        Assert.Equal(HttpStatusCode.OK, filed.StatusCode);
    }

    private async Task<AttributeView> AttributeAsync(FurnitureStore furniture, string name)
    {
        var attributes = await furniture.Admin.GetFromJsonAsync<List<AttributeView>>($"/api/admin/stores/{furniture.Store.StoreId}/attributes", CancellationToken);

        return attributes!.Single(attribute => attribute.Name == name);
    }

    private async Task<HttpResponseMessage> RenameOptionAsync(FurnitureStore furniture, AttributeView attribute, Guid option, string name) =>
        await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{attribute.Id}/options/{option}", new { Name = name }, CancellationToken);

    private async Task<HttpResponseMessage> SaveAttributeAsync(FurnitureStore furniture, AttributeView attribute, bool searchable) =>
        await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{attribute.Id}",
            new { attribute.Name, attribute.Unit, attribute.IsFilterable, attribute.IsVisibleOnProductPage, attribute.SortOrder, attribute.IsInFeeds, IsSearchable = searchable },
            CancellationToken);

    // The other store of the company sells the same product under a Czech name of its own, in the language given.
    private async Task SellInOtherStoreAsync(FurnitureStore furniture, string slug, string culture)
    {
        using var settings = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.OtherStore.StoreId}",
            new { furniture.OtherStore.Name, Culture = culture, Theme = new { PrimaryColor = "#112233", SecondaryColor = "#FFFFFF", BorderRadius = 4 } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, furniture.ProductIds[slug], "Dubová židle", 100m, slug: "dubova-zidle");
    }

    private static async Task<ImportReport> ImportAsync(FurnitureStore furniture, string[] columns, params object?[][] rows)
    {
        using var response = await furniture.Admin.ImportAsync(furniture.Store.StoreId, ImportFiles.Workbook(columns, rows));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ImportReport>(CancellationToken))!;
    }

    private sealed record VariantView(Guid Id);

    private sealed record ProductPage(List<PagedItem> Items);

    private sealed record PagedItem(string Slug);

    private sealed record ListingView(Guid Id, string Name, string Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder, string? SearchKeywords);

    private sealed record CategoryView(Guid Id, string? SearchKeywords);

    private sealed record AttributeView(Guid Id, string Name, string? Unit, bool IsFilterable, bool IsVisibleOnProductPage, int SortOrder, bool IsInFeeds, List<OptionView> Options);

    private sealed record OptionView(Guid Id, string Code);

    private sealed record ImportReport(int Created, int Updated, int Invalid, List<ImportIssue> Issues);

    private sealed record ImportIssue(int Row, string? Column, string Message);
}
