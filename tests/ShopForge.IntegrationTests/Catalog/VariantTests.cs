using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Catalog;

// A product is what a shopper looks at; a variant is what the warehouse stocks and the invoice names. Every
// product has at least one, and a shop that sells one form of a thing should not notice the difference (D-134).
public sealed class VariantTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_shirt_is_one_product_sold_in_three_sizes()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shirt = await ProductAsync(furniture, "SHIRT-S");

        var small = shirt.Variants.Single().Id;
        using var named = await SetOptionsAsync(furniture, shirt.Id, ["Size"], new Dictionary<Guid, string[]> { [small] = ["S"] });
        var medium = await AddVariantAsync(furniture, shirt.Id, "SHIRT-M", ["M"]);
        var large = await AddVariantAsync(furniture, shirt.Id, "SHIRT-L", ["L"]);
        var stored = await ProductAsync(furniture, shirt.Id);

        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal(["Size"], stored.OptionNames);
        Assert.Equal(["SHIRT-S", "SHIRT-M", "SHIRT-L"], stored.Variants.Select(variant => variant.Sku));
        Assert.Equal([["S"], ["M"], ["L"]], stored.Variants.Select(variant => variant.OptionValues));
        Assert.Equal([0, 1, 2], stored.Variants.Select(variant => variant.Position));
        Assert.Equal([medium, large], stored.Variants.Skip(1).Select(variant => variant.Id));
    }

    // Nothing in the admin had to change to keep working: a product created the way products were always created
    // is a product with one form, and the product still answers with its SKU, barcode and weight.
    [Fact]
    public async Task A_product_created_the_old_way_is_a_product_with_one_form()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var created = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/products", new { Sku = "plain-1", Ean = "8594000000001", WeightGrams = 850 }, CancellationToken);
        var product = await created.Content.ReadFromJsonAsync<ProductView>(CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("PLAIN-1", product!.Sku);
        Assert.Equal("8594000000001", product.Ean);
        Assert.Equal(850, product.WeightGrams);
        Assert.Empty(product.OptionNames);
        var only = Assert.Single(product.Variants);
        Assert.Equal(("PLAIN-1", "8594000000001", 850), (only.Sku, only.Ean, only.WeightGrams));
    }

    // A SKU is what a warehouse and an invoice call one thing, so it belongs to one thing in the whole company.
    [Fact]
    public async Task Two_forms_cannot_share_a_sku()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shirt = await ProductAsync(furniture, "TAKEN-1");
        var other = await ProductAsync(furniture, "TAKEN-2");

        using var sameProduct = await VariantResponseAsync(furniture, shirt.Id, "TAKEN-1", []);
        using var otherProduct = await VariantResponseAsync(furniture, other.Id, "TAKEN-1", []);
        using var freeName = await VariantResponseAsync(furniture, shirt.Id, "TAKEN-3", []);

        Assert.Equal(HttpStatusCode.Conflict, sameProduct.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherProduct.StatusCode);
        Assert.Equal(HttpStatusCode.Created, freeName.StatusCode);

        // The unique index would refuse these too, and says so in the language of a database. The check exists
        // to say it in the language of a shop, so that is what is asserted.
        Assert.Equal("A product with this SKU already exists", await TitleOfAsync(sameProduct));
        Assert.Equal("A product with this SKU already exists", await TitleOfAsync(otherProduct));
    }

    // A product with nothing behind it is a listing that cannot be bought, so the last form stays.
    [Fact]
    public async Task The_last_form_of_a_product_cannot_go()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shirt = await ProductAsync(furniture, "LAST-1");
        var second = await AddVariantAsync(furniture, shirt.Id, "LAST-2", []);

        using var spare = await furniture.Admin.DeleteAsync($"/api/admin/products/{shirt.Id}/variants/{second}", CancellationToken);
        var afterwards = await ProductAsync(furniture, shirt.Id);
        using var last = await furniture.Admin.DeleteAsync(
            $"/api/admin/products/{shirt.Id}/variants/{afterwards.Variants.Single().Id}", CancellationToken);
        var stillThere = await ProductAsync(furniture, shirt.Id);

        Assert.Equal(HttpStatusCode.NoContent, spare.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
        Assert.Single(stillThere.Variants);
    }

    // An axis without a value on every form would leave a shopper unable to say which one they want.
    [Fact]
    public async Task Naming_an_axis_needs_a_value_from_every_form()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shirt = await ProductAsync(furniture, "AXIS-1");
        var first = shirt.Variants.Single().Id;
        await AddVariantAsync(furniture, shirt.Id, "AXIS-2", []);

        using var half = await SetOptionsAsync(furniture, shirt.Id, ["Size"], new Dictionary<Guid, string[]> { [first] = ["S"] });
        var afterwards = await ProductAsync(furniture, shirt.Id);

        Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);
        Assert.Empty(afterwards.OptionNames);
    }

    // Not just "did every form answer" but "did it answer once per axis": a form with no value for Colour is as
    // unbuyable as a form nobody mentioned.
    [Fact]
    public async Task Naming_two_axes_needs_two_values_from_every_form()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shirt = await ProductAsync(furniture, "PAIR-1");
        var first = shirt.Variants.Single().Id;
        var second = await AddVariantAsync(furniture, shirt.Id, "PAIR-2", []);

        using var missing = await SetOptionsAsync(
            furniture,
            shirt.Id,
            ["Size", "Colour"],
            new Dictionary<Guid, string[]> { [first] = ["S", "Red"], [second] = ["M"] });
        using var whole = await SetOptionsAsync(
            furniture,
            shirt.Id,
            ["Size", "Colour"],
            new Dictionary<Guid, string[]> { [first] = ["S", "Red"], [second] = ["M", "Blue"] });
        var stored = await ProductAsync(furniture, shirt.Id);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal([["S", "Red"], ["M", "Blue"]], stored.Variants.Select(variant => variant.OptionValues));
    }

    [Fact]
    public async Task A_form_of_a_product_with_axes_has_to_say_which_one_it_is()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shirt = await ProductAsync(furniture, "SAYS-1");
        using var named = await SetOptionsAsync(
            furniture, shirt.Id, ["Size"], new Dictionary<Guid, string[]> { [shirt.Variants.Single().Id] = ["S"] });

        using var silent = await VariantResponseAsync(furniture, shirt.Id, "SAYS-2", []);
        using var spoken = await VariantResponseAsync(furniture, shirt.Id, "SAYS-2", ["M"]);

        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, silent.StatusCode);
        Assert.Equal(HttpStatusCode.Created, spoken.StatusCode);
    }

    // The barcode and the weight of a shirt are the barcode and weight of one size of it.
    [Fact]
    public async Task Once_there_is_more_than_one_form_the_barcode_belongs_to_the_form()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var shirt = await ProductAsync(furniture, "PHYS-1");

        using var whileSingle = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{shirt.Id}", new { Ean = "8594000000002", WeightGrams = 300 }, CancellationToken);
        var second = await AddVariantAsync(furniture, shirt.Id, "PHYS-2", []);
        using var whileSeveral = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{shirt.Id}", new { Ean = "8594000000003", WeightGrams = 400 }, CancellationToken);
        using var onTheForm = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{shirt.Id}/variants/{second}",
            new { Sku = "PHYS-2", Ean = "8594000000003", WeightGrams = 400, OptionValues = Array.Empty<string>() },
            CancellationToken);
        var stored = await ProductAsync(furniture, shirt.Id);

        Assert.Equal(HttpStatusCode.OK, whileSingle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, whileSeveral.StatusCode);
        Assert.Equal(HttpStatusCode.OK, onTheForm.StatusCode);
        Assert.Equal([300, 400], stored.Variants.Select(variant => variant.WeightGrams));
    }

    private static async Task<string?> TitleOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Problem>(CancellationToken))?.Title;

    private async Task<ProductView> ProductAsync(FurnitureStore furniture, string sku)
    {
        using var created = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/products", new { Sku = sku, Ean = (string?)null, WeightGrams = (int?)null }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await created.Content.ReadFromJsonAsync<ProductView>(CancellationToken))!;
    }

    private async Task<ProductView> ProductAsync(FurnitureStore furniture, Guid productId)
    {
        var products = await furniture.Admin.GetFromJsonAsync<List<ProductView>>("/api/admin/products", CancellationToken);

        return products!.Single(product => product.Id == productId);
    }

    private Task<HttpResponseMessage> SetOptionsAsync(
        FurnitureStore furniture, Guid productId, string[] names, Dictionary<Guid, string[]> values) =>
        furniture.Admin.PutAsJsonAsync($"/api/admin/products/{productId}/options", new { Names = names, Values = values }, CancellationToken);

    private Task<HttpResponseMessage> VariantResponseAsync(FurnitureStore furniture, Guid productId, string sku, string[] optionValues) =>
        furniture.Admin.PostAsJsonAsync(
            $"/api/admin/products/{productId}/variants",
            new { Sku = sku, Ean = (string?)null, WeightGrams = (int?)null, OptionValues = optionValues },
            CancellationToken);

    private async Task<Guid> AddVariantAsync(FurnitureStore furniture, Guid productId, string sku, string[] optionValues)
    {
        using var response = await VariantResponseAsync(furniture, productId, sku, optionValues);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<VariantView>(CancellationToken))!.Id;
    }

    private sealed record Problem(string? Title);

    private sealed record ProductView(
        Guid Id, string Sku, string? Ean, int? WeightGrams, List<string> OptionNames, List<VariantView> Variants);

    private sealed record VariantView(Guid Id, string Sku, string? Ean, int? WeightGrams, List<string> OptionValues, int Position);
}
