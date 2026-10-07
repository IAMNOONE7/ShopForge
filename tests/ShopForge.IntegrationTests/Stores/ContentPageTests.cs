using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Stores;

namespace ShopForge.IntegrationTests.Stores;

// A shop's own words: its terms, its privacy notice, what it charges for delivery, who it is. A page with text
// and not a CMS (D-175).
public sealed class ContentPageTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_published_page_is_served_with_what_the_merchant_wrote()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "terms", "Terms and conditions", "You may return anything within 14 days.\n\nWe deliver on Tuesdays.");

        var page = await ReadAsync(furniture, "terms");

        Assert.Equal("Terms and conditions", page.Title);
        Assert.Equal("You may return anything within 14 days.\n\nWe deliver on Tuesdays.", page.Body);
        Assert.Equal("terms", page.Slug);
    }

    // A draft is not a page that moved; it is a page that does not exist yet.
    [Fact]
    public async Task A_draft_is_not_served()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "about", "About us", "We have made chairs in Brno since 1972.", published: false);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var response = await shopper.GetAsync("/api/storefront/pages/about");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Publishing_a_draft_makes_it_appear_and_unpublishing_takes_it_away_again()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var pageId = await WriteAsync(furniture, "delivery", "Delivery", "Within two working days.", published: false);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var whileDraft = await shopper.GetAsync("/api/storefront/pages/delivery");
        await SaveAsync(furniture, pageId, "delivery", "Delivery", "Within two working days.", published: true);
        using var whenPublished = await shopper.GetAsync("/api/storefront/pages/delivery");
        await SaveAsync(furniture, pageId, "delivery", "Delivery", "Within two working days.", published: false);
        using var whenWithdrawn = await shopper.GetAsync("/api/storefront/pages/delivery");

        Assert.Equal(HttpStatusCode.NotFound, whileDraft.StatusCode);
        Assert.Equal(HttpStatusCode.OK, whenPublished.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, whenWithdrawn.StatusCode);
    }

    // The metadata chain and the canonical, on the same terms as every other page (D-165, D-174).
    [Fact]
    public async Task A_page_says_what_a_search_result_should_show_and_names_its_own_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "privacy", "Privacy notice", "We keep what we need to send you your order.");

        var page = await ReadAsync(furniture, "privacy");

        Assert.Equal("Privacy notice", page.Seo.Title);
        Assert.Equal($"https://{furniture.Store.HostName}/pages/privacy", page.Seo.Canonical);
        Assert.False(page.Seo.NoIndex);
    }

    [Fact]
    public async Task A_page_can_say_its_own_title_and_hide_itself()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var pageId = await WriteAsync(furniture, "internal", "Internal", "Not for shoppers.");
        await SaveAsync(furniture, pageId, "internal", "Internal", "Not for shoppers.", published: true,
            seo: new { Title = "Something else entirely", Description = "A description of its own.", NoIndex = true });

        var page = await ReadAsync(furniture, "internal");

        Assert.Equal("Something else entirely", page.Seo.Title);
        Assert.Equal("A description of its own.", page.Seo.Description);
        Assert.True(page.Seo.NoIndex);
    }

    // The slice called a slug collision with a product or a category the one real trap in a flat URL space.
    // The space is not flat: 28b gave each kind its own prefix, so the same word names three different pages
    // and all three work (D-149, D-175).
    [Fact]
    public async Task A_page_may_share_its_name_with_a_product_and_a_category()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "chairs", "About our chairs", "Every one is made by hand.");
        using var shopper = new StorefrontApi(factory, furniture.Store);

        var page = await ReadAsync(furniture, "chairs");
        var category = await shopper.GetJsonAsync<ListView>("/api/storefront/products?category=chairs");
        using var product = await shopper.GetAsync("/api/storefront/products/oak-chair");

        Assert.Equal(StorePages.ContentPage("chairs"), $"/pages/{page.Slug}");
        Assert.Equal("About our chairs", page.Title);
        Assert.Equal(3, category.TotalCount);
        Assert.Equal(HttpStatusCode.OK, product.StatusCode);
    }

    // Two pages cannot share one address within a shop, which is the collision that does exist.
    [Fact]
    public async Task Two_pages_of_one_shop_cannot_share_an_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "terms", "Terms", "The first.");

        using var second = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages",
            new { Slug = "terms", Title = "Terms again", Body = "The second.", IsPublished = true },
            CancellationToken);

        // The message matters as well as the code: the unique index would refuse this too, and the handler
        // that catches a race turns it into a conflict with a title nobody can act on. The page's own check is
        // what names the field a merchant has to change.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("slug is already used", await second.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // Two shops may both have a page called "terms", which is the point of the store being in the key.
    [Fact]
    public async Task One_shops_page_is_not_anothers()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "terms", "Our terms", "Ours.");

        using var theirs = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.OtherStore.StoreId}/pages",
            new { Slug = "terms", Title = "Their terms", Body = "Theirs.", IsPublished = true },
            CancellationToken);

        using var shopper = new StorefrontApi(factory, furniture.OtherStore);
        var page = await shopper.GetJsonAsync<ContentPageView>("/api/storefront/pages/terms");

        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);
        Assert.Equal("Their terms", page.Title);
        Assert.Equal("Our terms", (await ReadAsync(furniture, "terms")).Title);
    }

    [Fact]
    public async Task A_shop_lists_its_own_pages_and_nobody_elses()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "terms", "Terms", "Ours.");
        using var theirs = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.OtherStore.StoreId}/pages",
            new { Slug = "about", Title = "About them", Body = "Theirs.", IsPublished = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);

        var ours = await ListAsync(furniture, furniture.Store.StoreId);

        Assert.Equal(["Terms"], ours.Select(page => page.Title));
    }

    [Fact]
    public async Task A_page_can_be_renamed_and_rewritten()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var pageId = await WriteAsync(furniture, "delivery", "Delivery", "Two days.");

        await SaveAsync(furniture, pageId, "shipping", "Shipping", "Three days.", published: true);
        var page = await ReadAsync(furniture, "shipping");

        Assert.Equal("Shipping", page.Title);
        Assert.Equal("Three days.", page.Body);
    }

    [Fact]
    public async Task A_page_can_be_deleted()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var pageId = await WriteAsync(furniture, "temporary", "Temporary", "For now.");

        using var deleted = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages/{pageId}", CancellationToken);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var gone = await shopper.GetAsync("/api/storefront/pages/temporary");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Empty(await ListAsync(furniture, furniture.Store.StoreId));
    }

    [Fact]
    public async Task A_page_needs_a_title_and_a_slug_that_can_be_an_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var noTitle = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages",
            new { Slug = "terms", Title = " ", Body = "Text.", IsPublished = true },
            CancellationToken);
        using var badSlug = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages",
            new { Slug = "Terms & Conditions", Title = "Terms", Body = "Text.", IsPublished = true },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badSlug.StatusCode);
    }

    [Fact]
    public async Task A_slug_nobody_gave_is_made_from_the_title()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var created = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages",
            new { Title = "Terms and Conditions", Body = "Text.", IsPublished = true },
            CancellationToken);
        var page = await created.Content.ReadFromJsonAsync<AdminContentPageView>(CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("terms-and-conditions", page!.Slug);
    }

    // The body is text and never markup, so a merchant who types a tag gets a tag on the page rather than
    // running it (D-175).
    [Fact]
    public async Task A_tag_in_the_body_is_kept_as_the_words_the_merchant_typed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        const string Typed = "Read <b>carefully</b>, and <script>alert(1)</script> is not a tag we run.";
        await WriteAsync(furniture, "notice", "Notice", Typed);

        var page = await ReadAsync(furniture, "notice");

        Assert.Equal(Typed, page.Body);
    }

    [Fact]
    public async Task A_body_longer_than_a_page_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var tooLong = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages",
            new { Slug = "essay", Title = "Essay", Body = new string('a', 20_001), IsPublished = true },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    // The sitemap carries a shop's own pages, and only the ones it has published and not hidden (D-167).
    [Fact]
    public async Task Only_a_published_page_that_has_not_hidden_itself_is_in_the_sitemap()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await WriteAsync(furniture, "terms", "Terms", "Published.");
        await WriteAsync(furniture, "draft", "Draft", "Not published.", published: false);
        var hiddenId = await WriteAsync(furniture, "hidden", "Hidden", "Published but hidden.");
        await SaveAsync(furniture, hiddenId, "hidden", "Hidden", "Published but hidden.", published: true,
            seo: new { Title = (string?)null, Description = (string?)null, NoIndex = true });

        using var shopper = new StorefrontApi(factory, furniture.Store);
        var sitemap = await shopper.GetStringAsync("/api/storefront/sitemap.xml");

        Assert.Contains($"https://{furniture.Store.HostName}/pages/terms", sitemap, StringComparison.Ordinal);
        Assert.DoesNotContain("/pages/draft", sitemap, StringComparison.Ordinal);
        Assert.DoesNotContain("/pages/hidden", sitemap, StringComparison.Ordinal);
    }

    private async Task<Guid> WriteAsync(
        FurnitureStore furniture, string slug, string title, string body, bool published = true)
    {
        using var created = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages",
            new { Slug = slug, Title = title, Body = body, IsPublished = published },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await created.Content.ReadFromJsonAsync<AdminContentPageView>(CancellationToken))!.Id;
    }

    private async Task SaveAsync(
        FurnitureStore furniture, Guid pageId, string slug, string title, string body, bool published, object? seo = null)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages/{pageId}",
            new { Slug = slug, Title = title, Body = body, IsPublished = published, Seo = seo },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<ContentPageView> ReadAsync(FurnitureStore furniture, string slug)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);

        return await shopper.GetJsonAsync<ContentPageView>($"/api/storefront/pages/{slug}");
    }

    private async Task<List<AdminContentPageView>> ListAsync(FurnitureStore furniture, Guid storeId) =>
        (await furniture.Admin.GetFromJsonAsync<List<AdminContentPageView>>(
            $"/api/admin/stores/{storeId}/pages", CancellationToken))!;

    private sealed record ContentPageView(string Slug, string Title, string Body, PageSeoView Seo);

    private sealed record PageSeoView(string Title, string? Description, bool NoIndex, string? Canonical);

    private sealed record AdminContentPageView(Guid Id, string Slug, string Title, string Body, bool IsPublished);

    private sealed record ListView(int TotalCount);
}
