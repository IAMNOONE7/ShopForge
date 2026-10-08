using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Admin;

// An attribute or an option can be corrected without starting again. The type stays fixed (D-035), and so
// does the code — it is an address rather than a label (D-181).
public sealed class AttributeEditingTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_options_words_can_be_corrected_without_moving_its_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var oak = material.Options.Single(option => option.Code == "oak");

        await RenameOptionAsync(furniture, material.Id, oak.Id, "Solid oak");
        var after = await AttributeAsync(furniture, "Material");

        Assert.Equal("Solid oak", after.Options.Single(option => option.Code == "oak").Name);
        Assert.Equal("oak", after.Options.Single(option => option.Name == "Solid oak").Code);
    }

    // The code is what a bookmarked filter and a published feed name, so correcting the words leaves the
    // shopper's own link working.
    [Fact]
    public async Task A_renamed_option_still_answers_to_the_filter_it_always_did()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var oak = material.Options.Single(option => option.Code == "oak");

        await RenameOptionAsync(furniture, material.Id, oak.Id, "Solid oak");
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var filtered = await shopper.GetJsonAsync<ProductPage>("/api/storefront/products?f.material=oak");

        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(
            "Solid oak",
            filtered.Filters.Single(filter => filter.Code == "material").Options!.Single(option => option.Code == "oak").Name);
    }

    [Fact]
    public async Task An_options_name_cannot_be_blanked()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var oak = material.Options.Single(option => option.Code == "oak");

        using var refused = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{material.Id}/options/{oak.Id}",
            new { Name = "   " },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // The order a shopper reads the choices in.
    [Fact]
    public async Task Options_can_be_put_in_the_order_a_shopper_should_read_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var walnut = material.Options.Single(option => option.Code == "walnut");
        var beech = material.Options.Single(option => option.Code == "beech");

        await ReorderAsync(furniture, material.Id, [walnut.Id, beech.Id]);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var facet = (await shopper.GetJsonAsync<ProductPage>("/api/storefront/products?category=chairs"))
            .Filters.Single(filter => filter.Code == "material");

        Assert.Equal(["walnut", "beech", "oak"], facet.Options!.Select(option => option.Code));
    }

    // Naming two of three moves those two and leaves the rest in the order they had, rather than scrambling
    // what the caller said nothing about.
    [Fact]
    public async Task Options_the_caller_does_not_name_keep_their_order_after_the_ones_it_does()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var colors = await AttributeAsync(furniture, "Colors");
        var natural = colors.Options.Single(option => option.Code == "natural");

        await ReorderAsync(furniture, colors.Id, [natural.Id]);
        var after = await AttributeAsync(furniture, "Colors");

        Assert.Equal(["natural", "black", "white"], after.Options.OrderBy(option => option.SortOrder).Select(option => option.Code));
    }

    [Fact]
    public async Task An_option_nobody_has_chosen_can_be_removed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var spare = await AddOptionAsync(furniture, material.Id, "Ash");

        using var removed = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{material.Id}/options/{spare}", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain("ash", (await AttributeAsync(furniture, "Material")).Options.Select(option => option.Code));
    }

    // A listing set to it would be pointing at nothing, and the count says what changing them costs.
    [Fact]
    public async Task An_option_a_listing_is_set_to_cannot_be_removed_and_the_reason_counts_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var oak = material.Options.Single(option => option.Code == "oak");

        using var refused = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{material.Id}/options/{oak.Id}", CancellationToken);
        var problem = await refused.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("2 listings are set to it", problem, StringComparison.Ordinal);
        Assert.Contains("Change those listings first", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_listing_set_to_an_option_is_counted_in_the_singular()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var walnut = material.Options.Single(option => option.Code == "walnut");

        using var refused = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{material.Id}/options/{walnut.Id}", CancellationToken);

        Assert.Contains("1 listing is set to it", await refused.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // A measurement nobody has filled in is a mistake to take away.
    [Fact]
    public async Task An_attribute_nobody_has_used_can_be_removed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var spare = await furniture.Admin.CreateAttributeAsync(
            furniture.Store.StoreId, new { Name = "Spare", Type = "text", IsVisibleOnProductPage = true });

        using var removed = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{spare}", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain("Spare", (await AttributesAsync(furniture)).Select(attribute => attribute.Name));
    }

    [Fact]
    public async Task An_attribute_with_values_in_it_cannot_be_removed_and_the_reason_counts_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var width = await AttributeAsync(furniture, "Width");

        using var refused = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{width.Id}", CancellationToken);
        var problem = await refused.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("listings have a value for it", problem, StringComparison.Ordinal);
        Assert.Contains("Clear them first", problem, StringComparison.Ordinal);
    }

    // A category offering it as a filter is the other thing that holds it, and it is reported when no listing
    // does — a merchant is not helped by being told two things at once.
    [Fact]
    public async Task An_attribute_a_category_offers_as_a_filter_cannot_be_removed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var spare = await furniture.Admin.CreateAttributeAsync(
            furniture.Store.StoreId, new { Name = "Finish", Type = "select", IsFilterable = true, Options = new[] { "Oiled" } });
        using var assigned = await furniture.Admin.AssignCategoryAttributesAsync(
            furniture.Store.StoreId, furniture.ChairsCategoryId, spare);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        using var refused = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{spare}", CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("offers it as a filter", await refused.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // D-035 fixed the type on purpose: the values are stored in a column chosen by it, so changing it would
    // orphan every one of them.
    [Fact]
    public async Task An_attributes_type_cannot_be_changed_by_saving_it_again()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var width = await AttributeAsync(furniture, "Width");

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{width.Id}",
            new { width.Name, Type = "text", width.Unit, width.IsFilterable, width.IsVisibleOnProductPage, width.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("decimal", (await AttributeAsync(furniture, "Width")).Type);
    }

    // The code is likewise not something a save can move.
    [Fact]
    public async Task An_attributes_code_cannot_be_changed_by_saving_it_again()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{material.Id}",
            new { Name = "Timber", material.Unit, material.IsFilterable, material.IsVisibleOnProductPage, material.SortOrder },
            CancellationToken);
        var after = await AttributesAsync(furniture);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("material", after.Single(attribute => attribute.Name == "Timber").Code);
    }

    [Fact]
    public async Task Editing_an_attribute_is_written_down()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var material = await AttributeAsync(furniture, "Material");
        var oak = material.Options.Single(option => option.Code == "oak");

        await RenameOptionAsync(furniture, material.Id, oak.Id, "Solid oak");
        await ReorderAsync(furniture, material.Id, [oak.Id]);

        var recorded = await factory.EventuallyAsync(
            () => furniture.Admin.GetFromJsonAsync<List<AuditRow>>("/api/admin/audit", CancellationToken),
            entries => entries!.Any(entry => entry.Action == "catalog.attribute.options.reordered"),
            CancellationToken);
        var actions = recorded!.Select(entry => entry.Action).ToList();

        Assert.Contains("catalog.attribute.option.renamed", actions);
        Assert.Contains("catalog.attribute.options.reordered", actions);
    }

    private async Task RenameOptionAsync(FurnitureStore furniture, Guid attributeId, Guid optionId, string name)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{attributeId}/options/{optionId}",
            new { Name = name },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task ReorderAsync(FurnitureStore furniture, Guid attributeId, Guid[] optionIds)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{attributeId}/options",
            new { OptionIds = optionIds },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<Guid> AddOptionAsync(FurnitureStore furniture, Guid attributeId, string name)
    {
        using var added = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{attributeId}/options",
            new { Name = name },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var attribute = (await added.Content.ReadFromJsonAsync<AttributeRow>(CancellationToken))!;

        return attribute.Options.Single(option => option.Name == name).Id;
    }

    private async Task<AttributeRow> AttributeAsync(FurnitureStore furniture, string name) =>
        (await AttributesAsync(furniture)).Single(attribute => attribute.Name == name);

    private async Task<List<AttributeRow>> AttributesAsync(FurnitureStore furniture) =>
        (await furniture.Admin.GetFromJsonAsync<List<AttributeRow>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes", CancellationToken))!;

    private sealed record AttributeRow(
        Guid Id, string Code, string Name, string Type, string? Unit, bool IsFilterable, bool IsVisibleOnProductPage,
        int SortOrder, List<OptionRow> Options);

    private sealed record OptionRow(Guid Id, string Code, string Name, int SortOrder);

    private sealed record ProductPage(int TotalCount, List<Facet> Filters);

    private sealed record Facet(string Code, List<FacetOption>? Options);

    private sealed record FacetOption(string Code, string Name, int Count);

    private sealed record AuditRow(string Action, string Subject);
}
