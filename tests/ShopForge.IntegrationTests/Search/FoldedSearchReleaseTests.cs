using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Search;

// What a release that adds the folded vector needs on both sides of it: every listing from before it found without
// accents once it has run, and the vector the previous build reads still kept up for a rollback (D-224).
public sealed class FoldedSearchReleaseTests(ShopForgeApiFactory factory)
{
    private const string BeforeFolding = "20261029134918_FreeShipping";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Every other test starts from an empty database, where seeding nothing looks the same as not seeding at all, so
    // this one starts from a catalogue of the shape before the migration, in a Czech store and an English one.
    [Fact]
    public async Task A_catalogue_from_before_folding_is_found_without_accents_each_store_in_its_own_language()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);
        await database.MigrateAsync(BeforeFolding);
        await database.ExecuteAsync(Catalogue(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()));

        await database.MigrateAsync();

        // Czech is read without stemming: English rules would have made `šroubováky` into `sroubovaki`.
        Assert.Equal(["sroubovaky-ph2"], await FoundAsync(database, "catalog.simple_folded", "sroubovaky"));
        Assert.Equal(["sroubovaky-ph2"], await FoundAsync(database, "catalog.simple_folded", "magneticky"));
        Assert.Equal(["sroubovaky-ph2"], await FoundAsync(database, "catalog.simple_folded", "vanadova"));
        Assert.Equal(["sroubovaky-ph2"], await FoundAsync(database, "catalog.simple_folded", "TS-PH2-100"));

        // English is stemmed: without its own rules `rocking` would have stayed whole and missed `rock`.
        Assert.Equal(["rocking-chairs"], await FoundAsync(database, "catalog.english_folded", "rocked"));
    }

    // Until V29i drops it, the vector the previous build reads is written beside the folded one, so a build brought
    // back by a rollback finds what changed while the release served.
    [Fact]
    public async Task The_vector_a_previous_build_reads_still_follows_every_change()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var imported = await furniture.Admin.ImportAsync(
            furniture.Store.StoreId,
            ImportFiles.MeasuredWorkbook(["sku", "name"], [FurnitureStore.SkuOf("walnut-chair"), "Mahogany Throne"]));
        var report = await imported.Content.ReadFromJsonAsync<ImportReport>(CancellationToken);

        var found = await factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT slug AS \"Value\" FROM catalog.store_products WHERE store_id = {furniture.Store.StoreId} AND search_vector @@ websearch_to_tsquery('english', 'mahogany')")
            .ToListAsync(CancellationToken));

        Assert.Equal((HttpStatusCode.OK, 1), (imported.StatusCode, report!.Updated));
        Assert.Equal(["walnut-chair"], found);
    }

    private static Task<List<string>> FoundAsync(OwnDatabase database, string configuration, string typed) =>
        database.TextAsync(
            $"SELECT slug AS \"Value\" FROM catalog.store_products WHERE search_vector_folded @@ websearch_to_tsquery({configuration}::regconfig, {typed}) ORDER BY slug");

    // One company's two stores, each selling one thing: a name and a code, and for the Czech one a description and a
    // searchable attribute.
    private static string Catalogue(Guid tenant, Guid czech, Guid english)
    {
        var (screwdriver, chair) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (screwdriverListing, attribute) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        return $"""
            INSERT INTO stores.tenants (id, name) VALUES ('{tenant}', 'Tenant');
            INSERT INTO stores.stores (id, tenant_id, name, currency, culture, theme, status) VALUES
                ('{czech}', '{tenant}', 'Nářadí', 'CZK', 'cs-CZ', jsonb_build_object(), 'Published'),
                ('{english}', '{tenant}', 'Furniture', 'EUR', 'en-IE', jsonb_build_object(), 'Published');
            INSERT INTO catalog.products (id, tenant_id) VALUES ('{screwdriver}', '{tenant}'), ('{chair}', '{tenant}');
            INSERT INTO catalog.product_variants (id, tenant_id, product_id, sku, option_values, position) VALUES
                ('{Guid.CreateVersion7()}', '{tenant}', '{screwdriver}', 'TS-PH2-100', ARRAY[]::text[], 0),
                ('{Guid.CreateVersion7()}', '{tenant}', '{chair}', 'FURN-ROCKER', ARRAY[]::text[], 0);
            INSERT INTO catalog.store_products (id, store_id, product_id, name, slug, description, price, is_visible, sort_order, vat_rate) VALUES
                ('{screwdriverListing}', '{czech}', '{screwdriver}', 'Šroubováky PH2', 'sroubovaky-ph2', 'Magnetický hrot.', 199, true, 0, 21),
                ('{Guid.CreateVersion7()}', '{english}', '{chair}', 'Rocking Chairs', 'rocking-chairs', NULL, 300, true, 0, 21);
            INSERT INTO catalog.attribute_definitions (id, store_id, code, name, type, is_filterable, is_visible_on_product_page, sort_order, is_searchable) VALUES
                ('{attribute}', '{czech}', 'material', 'Materiál', 'Text', false, true, 0, true);
            INSERT INTO catalog.product_attribute_values (id, store_id, store_product_id, attribute_definition_id, text_value) VALUES
                ('{Guid.CreateVersion7()}', '{czech}', '{screwdriverListing}', '{attribute}', 'Chrom-vanadová ocel');
            """;
    }

    private sealed record ImportReport(int Created, int Updated, int Invalid);
}
