using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Catalog;

// A merchant writes what a search result shows, and the platform has an answer when they have not (D-165).
public sealed class PageMetadataTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_product_page_falls_back_to_the_stores_answer()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeStoreAsync(furniture, "Acme Furniture", "Chairs made in Brno.", "https://acme.test/social.png", noIndex: false);

        var page = await PageAsync(furniture, "/api/storefront/products/oak-chair");

        Assert.Equal("Oak Chair — Acme Furniture", page.Seo.Title);
        Assert.Equal("Chairs made in Brno.", page.Seo.Description);
        Assert.Equal("https://acme.test/social.png", page.Seo.SocialImageUrl);
        Assert.False(page.Seo.NoIndex);
    }

    [Fact]
    public async Task A_listing_that_says_something_of_its_own_is_believed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeStoreAsync(furniture, "Acme Furniture", "Chairs made in Brno.", null, noIndex: false);
        await DescribeListingAsync(furniture, "oak-chair", "Buy an oak chair today", "One chair, many years.", noIndex: false);

        var page = await PageAsync(furniture, "/api/storefront/products/oak-chair");

        Assert.Equal("Buy an oak chair today", page.Seo.Title);
        Assert.Equal("One chair, many years.", page.Seo.Description);
    }

    // A shop that is not ready to be found hides all of itself, whatever any one page says.
    [Fact]
    public async Task A_store_that_hides_itself_hides_every_page_of_itself()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeStoreAsync(furniture, null, null, null, noIndex: true);

        var product = await PageAsync(furniture, "/api/storefront/products/oak-chair");
        var list = await ListAsync(furniture, "/api/storefront/products");

        Assert.True(product.Seo.NoIndex);
        Assert.True(list.Seo.NoIndex);
    }

    [Fact]
    public async Task A_listing_can_hide_itself_in_a_shop_that_is_otherwise_open()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeListingAsync(furniture, "oak-chair", null, null, noIndex: true);

        var hidden = await PageAsync(furniture, "/api/storefront/products/oak-chair");
        var other = await PageAsync(furniture, "/api/storefront/products/walnut-chair");

        Assert.True(hidden.Seo.NoIndex);
        Assert.False(other.Seo.NoIndex);
    }

    // A category page is the product list filtered by one category, so that is where its words belong.
    [Fact]
    public async Task A_category_page_carries_its_own_title_and_the_words_above_the_products()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeStoreAsync(furniture, "Acme Furniture", null, null, noIndex: false);
        await DescribeCategoryAsync(furniture, "Chairs to sit on", "Every chair we make.", "We have made chairs in Brno since 1972.");

        var page = await ListAsync(furniture, "/api/storefront/products?category=chairs");
        var unfiltered = await ListAsync(furniture, "/api/storefront/products");

        Assert.Equal("Chairs to sit on", page.Seo.Title);
        Assert.Equal("Every chair we make.", page.Seo.Description);
        Assert.Equal("We have made chairs in Brno since 1972.", page.PageText);
        Assert.Equal($"{furniture.Store.Name} — Acme Furniture", unfiltered.Seo.Title);
        Assert.Null(unfiltered.PageText);
    }

    [Theory]
    [InlineData("titleSuffix", 201)]
    [InlineData("description", 501)]
    public async Task Something_longer_than_the_column_is_refused(string field, int length)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var tooLong = new string('x', length);
        var seo = field == "titleSuffix"
            ? new { TitleSuffix = (string?)tooLong, Description = (string?)null, SocialImageUrl = (string?)null, NoIndex = false }
            : new { TitleSuffix = (string?)null, Description = (string?)tooLong, SocialImageUrl = (string?)null, NoIndex = false };

        using var refused = await SaveStoreAsync(furniture, seo);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // A social image is published to whoever shares a link, so a relative path is no use to them.
    [Fact]
    public async Task A_social_image_that_is_not_an_address_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await SaveStoreAsync(
            furniture,
            new { TitleSuffix = (string?)null, Description = (string?)null, SocialImageUrl = "/images/social.png", NoIndex = false });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    private Task<HttpResponseMessage> SaveStoreAsync(FurnitureStore furniture, object seo) =>
        furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}",
            new
            {
                Name = furniture.Store.Name,
                Currency = "EUR",
                Culture = "en-IE",
                Theme = new { PrimaryColor = "#112233", SecondaryColor = "#FFFFFF", BorderRadius = 4 },
                Seo = seo,
            },
            CancellationToken);

    private async Task DescribeStoreAsync(FurnitureStore furniture, string? suffix, string? description, string? image, bool noIndex)
    {
        using var saved = await SaveStoreAsync(
            furniture,
            new { TitleSuffix = suffix, Description = description, SocialImageUrl = image, NoIndex = noIndex });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task DescribeListingAsync(FurnitureStore furniture, string slug, string? title, string? description, bool noIndex)
    {
        var storeProductId = furniture.Products[slug];
        var listings = await furniture.Admin.GetFromJsonAsync<List<ListingView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products", CancellationToken);
        var listing = listings!.Single(candidate => candidate.Id == storeProductId);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{storeProductId}",
            new
            {
                listing.Name,
                listing.Slug,
                listing.Description,
                listing.Price,
                listing.VatRate,
                listing.IsVisible,
                listing.SortOrder,
                Seo = new { Title = title, Description = description, SocialImageUrl = (string?)null, NoIndex = noIndex },
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task DescribeCategoryAsync(FurnitureStore furniture, string? title, string? description, string? pageText)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{furniture.ChairsCategoryId}",
            new
            {
                Name = "Chairs",
                Slug = "chairs",
                SortOrder = 0,
                Seo = new { Title = title, Description = description, PageText = pageText },
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<DetailView> PageAsync(FurnitureStore furniture, string path)
    {
        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);

        return await shopper.GetJsonAsync<DetailView>(path);
    }

    private async Task<ListView> ListAsync(FurnitureStore furniture, string path)
    {
        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);

        return await shopper.GetJsonAsync<ListView>(path);
    }

    private sealed record SeoView(string Title, string? Description, string? SocialImageUrl, bool NoIndex);

    private sealed record DetailView(string Slug, SeoView Seo);

    private sealed record ListView(SeoView Seo, string? PageText);

    private sealed record ListingView(Guid Id, string Name, string Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder);
}
