using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Admin;

// Pictures in the order the merchant wants, each described (D-182).
public sealed class ProductImageTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pictures_arrive_in_the_order_they_were_uploaded()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        await UploadAsync(furniture, product, "The front");
        await UploadAsync(furniture, product, "The back");

        var images = await ImagesAsync(furniture, product);

        Assert.Equal(["The front", "The back"], images.Select(image => image.AltText));
        Assert.Equal([0, 1], images.Select(image => image.Position));
    }

    // The first picture is the thumbnail: it is the one a card shows and the one a shopping feed carries.
    [Fact]
    public async Task Reordering_decides_which_picture_is_the_thumbnail()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var front = await UploadAsync(furniture, product, "The front");
        var back = await UploadAsync(furniture, product, "The back");

        await ReorderAsync(furniture, product, [back, front]);
        var images = await ImagesAsync(furniture, product);

        Assert.Equal(["The back", "The front"], images.Select(image => image.AltText));
        Assert.Equal([0, 1], images.Select(image => image.Position));
    }

    // Naming one of three moves that one and leaves the others in the order they had, the way an attribute's
    // options do (D-181).
    [Fact]
    public async Task Pictures_the_caller_does_not_name_keep_their_order_behind_the_ones_it_does()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        await UploadAsync(furniture, product, "First");
        await UploadAsync(furniture, product, "Second");
        var third = await UploadAsync(furniture, product, "Third");

        await ReorderAsync(furniture, product, [third]);
        var images = await ImagesAsync(furniture, product);

        Assert.Equal(["Third", "First", "Second"], images.Select(image => image.AltText));
    }

    // The order the shopper sees, which is the point of all of it.
    [Fact]
    public async Task The_shop_shows_the_pictures_in_the_order_the_merchant_chose()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var front = await UploadAsync(furniture, product, "The front");
        var back = await UploadAsync(furniture, product, "The back");
        await ReorderAsync(furniture, product, [back, front]);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        var page = await shopper.GetJsonAsync<ProductDetail>("/api/storefront/products/oak-chair");

        Assert.Equal(["The back", "The front"], page.Images.Select(image => image.AltText));
    }

    [Fact]
    public async Task A_picture_can_be_described_after_it_was_uploaded()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var image = await UploadAsync(furniture, product, altText: null);

        await DescribeAsync(furniture, product, image, "An oak chair seen from the front");
        var images = await ImagesAsync(furniture, product);

        Assert.Equal("An oak chair seen from the front", images.Single().AltText);
    }

    [Fact]
    public async Task A_description_can_be_taken_away_again()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var image = await UploadAsync(furniture, product, "Wrong words");

        await DescribeAsync(furniture, product, image, "   ");
        var images = await ImagesAsync(furniture, product);

        Assert.Null(images.Single().AltText);
    }

    [Fact]
    public async Task A_description_longer_than_a_sentence_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var image = await UploadAsync(furniture, product, "The front");

        using var refused = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{product}/images/{image}",
            new { AltText = new string('a', 201) },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_described_picture_reaches_the_shop()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var image = await UploadAsync(furniture, product, altText: null);
        await DescribeAsync(furniture, product, image, "An oak chair");

        using var shopper = new StorefrontApi(factory, furniture.Store);
        var page = await shopper.GetJsonAsync<ProductDetail>("/api/storefront/products/oak-chair");

        Assert.Equal("An oak chair", page.Images.Single().AltText);
    }

    // Deleting one renumbers the rest in the order they were in. Renumbering by whatever order they happened
    // to be loaded in would reshuffle the pictures every time one was removed (D-182).
    [Fact]
    public async Task Deleting_a_picture_leaves_the_others_in_the_order_they_were_in()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var first = await UploadAsync(furniture, product, "First");
        var second = await UploadAsync(furniture, product, "Second");
        var third = await UploadAsync(furniture, product, "Third");

        // Put them in an order that is not the one they were uploaded in, so the renumbering has something to
        // get wrong.
        await ReorderAsync(furniture, product, [third, first, second]);
        using var deleted = await furniture.Admin.DeleteAsync(
            $"/api/admin/products/{product}/images/{first}", CancellationToken);
        var images = await ImagesAsync(furniture, product);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["Third", "Second"], images.Select(image => image.AltText));
        Assert.Equal([0, 1], images.Select(image => image.Position));
    }

    [Fact]
    public async Task Describing_and_reordering_are_written_down()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = furniture.ProductIds["oak-chair"];
        var image = await UploadAsync(furniture, product, altText: null);

        await DescribeAsync(furniture, product, image, "An oak chair");
        await ReorderAsync(furniture, product, [image]);

        var recorded = await factory.EventuallyAsync(
            () => furniture.Admin.GetFromJsonAsync<List<AuditRow>>("/api/admin/audit", CancellationToken),
            entries => entries!.Any(entry => entry.Action == "catalog.product.images.reordered"),
            CancellationToken);
        var actions = recorded!.Select(entry => entry.Action).ToList();

        Assert.Contains("catalog.product.image.described", actions);
        Assert.Contains("catalog.product.images.reordered", actions);
    }

    private async Task<Guid> UploadAsync(FurnitureStore furniture, Guid productId, string? altText)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(AdminCatalogApi.PngBytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");

        if (altText is not null)
        {
            form.Add(new StringContent(altText), "altText");
        }

        using var uploaded = await furniture.Admin.PostAsync($"/api/admin/products/{productId}/images", form, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);

        return (await uploaded.Content.ReadFromJsonAsync<ImageRow>(CancellationToken))!.Id;
    }

    private async Task ReorderAsync(FurnitureStore furniture, Guid productId, Guid[] imageIds)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}/images", new { ImageIds = imageIds }, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task DescribeAsync(FurnitureStore furniture, Guid productId, Guid imageId, string altText)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}/images/{imageId}", new { AltText = altText }, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<List<ImageRow>> ImagesAsync(FurnitureStore furniture, Guid productId)
    {
        var products = await furniture.Admin.GetFromJsonAsync<List<ProductRow>>("/api/admin/products", CancellationToken);

        return [.. products!.Single(product => product.Id == productId).Images.OrderBy(image => image.Position)];
    }

    private sealed record ProductRow(Guid Id, List<ImageRow> Images);

    private sealed record ImageRow(Guid Id, string Url, string? AltText, int Position);

    private sealed record ProductDetail(string Slug, List<ImageView> Images);

    private sealed record ImageView(string Url, string? AltText);

    private sealed record AuditRow(string Action, string Subject);
}
