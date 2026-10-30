using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Search;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Search;

// Whatever accents are typed or stored, the same words find the same products: `sroubovak`, `šroubovák` and
// `ŠROUBOVÁK` are one word (D-224). Czech has no stemmer, so its words are folded and kept whole; English is
// stemmed, and folded before it is, so each test runs in a store of each.
public sealed class AccentFoldingTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-IE")]
    public async Task A_word_finds_the_same_products_in_the_same_order_however_its_accents_are_typed(string culture)
    {
        var furniture = await FurnitureStore.CreateAsync(factory, culture);
        await RenameAsync(furniture, "oak-chair", "Šroubovák plochý");

        // The one that only mentions it comes first in the shop's own order, so only the ranking puts the name first.
        await DescribeAsync(furniture, "walnut-chair", "Šroubovák v sadě s bity.", sortOrder: -5);

        var found = new Dictionary<string, List<string>>();

        foreach (var typed in new[] { "šroubovák", "sroubovak", "Šroubovák", "ŠROUBOVÁK", "SROUBOVAK" })
        {
            found[typed] = [.. (await SearchAsync(furniture, typed)).Items.Select(item => item.Slug)];
        }

        Assert.All(found, answer => Assert.Equal(["oak-chair", "walnut-chair"], answer.Value));
    }

    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-IE")]
    public async Task Words_stored_without_accents_are_found_by_the_same_words_typed_with_them(string culture)
    {
        var furniture = await FurnitureStore.CreateAsync(factory, culture);
        await RenameAsync(furniture, "beech-stool", "Kuchynska stolicka");

        var found = await SearchAsync(furniture, "kuchyňská stolička");

        Assert.Equal(["beech-stool"], found.Items.Select(item => item.Slug));
    }

    // Slovak's letters, and the rest of what `unaccent` knows: German ß, Polish ł, Danish ø.
    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-IE")]
    public async Task Slovak_letters_and_the_others_unaccent_knows_fold_too(string culture)
    {
        var furniture = await FurnitureStore.CreateAsync(factory, culture);
        await DescribeAsync(furniture, "oak-chair", "Stôl z vŕby, stĺp, ľan a mäso. Fußhocker, łóżko, ølglas.");

        var found = new Dictionary<string, List<string>>();

        foreach (var typed in new[] { "stol", "vrby", "stlp", "lan", "maso", "fusshocker", "lozko", "olglas" })
        {
            found[typed] = [.. (await SearchAsync(furniture, typed)).Items.Select(item => item.Slug)];
        }

        Assert.All(found, answer => Assert.Equal(["oak-chair"], answer.Value));
    }

    // A code is a word in no language, and folding changes nothing about how one is found.
    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-IE")]
    public async Task A_code_is_still_found_exactly(string culture)
    {
        var furniture = await FurnitureStore.CreateAsync(factory, culture);
        await RenameAsync(furniture, "walnut-chair", "Šroubovák PH2");
        var screw = await furniture.Admin.CreateProductAsync("ŠROUB-Ø8");
        await furniture.Admin.ListProductAsync(furniture.Store.StoreId, screw, "Vrut do dřeva", 4m, slug: "vrut");

        var bySku = await SearchAsync(furniture, FurnitureStore.SkuOf("oak-chair"));
        var byModel = await SearchAsync(furniture, "PH2");
        var byAccentedSku = await SearchAsync(furniture, "SROUB-O8");

        Assert.Equal(["oak-chair"], bySku.Items.Select(item => item.Slug));
        Assert.Equal(["walnut-chair"], byModel.Items.Select(item => item.Slug));
        Assert.Equal(["vrut"], byAccentedSku.Items.Select(item => item.Slug));
    }

    // Some keyboards and pasted text send a letter and its accent as two characters. It is the same letter, so it is
    // the same search, and the log has it once rather than as a word nobody else typed.
    [Fact]
    public async Task A_letter_sent_as_a_letter_and_an_accent_is_the_letter()
    {
        var furniture = await FurnitureStore.CreateAsync(factory, "cs-CZ");
        await RenameAsync(furniture, "oak-chair", "Šroubovák plochý");
        var composed = "šroubovák".Normalize(NormalizationForm.FormC);
        var decomposed = composed.Normalize(NormalizationForm.FormD);

        var found = await SearchAsync(furniture, decomposed);
        var logged = await LoggedAsync(furniture);

        Assert.NotEqual(composed, decomposed);
        Assert.Equal(["oak-chair"], found.Items.Select(item => item.Slug));
        Assert.Equal([composed], logged);
    }

    // An emoji is two UTF-16 units. Cutting a long search between them would leave half a character, which is not
    // text and which the database's driver refuses to send.
    [Fact]
    public async Task A_long_search_is_cut_between_characters_rather_than_inside_one()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var emoji = await shopper.GetAsync(
            $"/api/storefront/products?q={Uri.EscapeDataString(new string('a', SearchTerms.Longest - 1) + "😀")}");
        using var letters = await shopper.GetAsync($"/api/storefront/products?q={new string('b', SearchTerms.Longest + 50)}");
        var logged = await LoggedAsync(furniture);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (emoji.StatusCode, letters.StatusCode));
        Assert.Equal(
            [new string('a', SearchTerms.Longest - 1), new string('b', SearchTerms.Longest)],
            logged.Order(StringComparer.Ordinal));
    }

    // A query is read in the store's new language the moment the change is saved, so its listings are read again in
    // it too, rather than half of search missing until the nightly pass. English reads "Rocking Chairs" as "rock" and
    // "chair"; Czech, without a stemmer, needs the words as written.
    [Fact]
    public async Task A_store_that_changes_its_language_is_searched_in_the_new_one_straight_away()
    {
        var furniture = await FurnitureStore.CreateAsync(factory, "en-IE");
        await RenameAsync(furniture, "oak-chair", "Rocking Chairs");

        await ChangeCultureAsync(furniture, "cs-CZ");
        var found = await factory.EventuallyAsync(() => SearchAsync(furniture, "rocking"), page => page.Items.Count > 0, CancellationToken);

        Assert.Equal(["oak-chair"], found.Items.Select(item => item.Slug));
    }

    // Reading a whole catalogue again costs something, so saving the settings with the same language asks for nothing.
    [Fact]
    public async Task Only_a_change_of_language_reads_the_catalogue_again()
    {
        var furniture = await FurnitureStore.CreateAsync(factory, "en-IE");

        await ChangeCultureAsync(furniture, "en-IE");
        var unchanged = await CultureChangesAsync(furniture);
        await ChangeCultureAsync(furniture, "cs-CZ");
        var changed = await CultureChangesAsync(furniture);

        Assert.Equal((0, 1), (unchanged, changed));
    }

    // Each culture's folded configuration is its language's with `unaccent` in front of the dictionary of every kind
    // of word that can carry an accent, and identical otherwise: the same stemming, the same stop words. One check
    // over all of them, against the migrated database, so a language added to `SearchConfigurations` without a
    // configuration of its own fails here rather than in a shop.
    [Fact]
    public async Task Every_culture_s_folded_configuration_is_its_language_s_with_accents_folded_first()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<DbContext>().Database;

        var folded = await database.SqlQuery<string>(
            $"SELECT 'catalog.' || cfgname AS \"Value\" FROM pg_ts_config WHERE cfgnamespace = 'catalog'::regnamespace ORDER BY cfgname")
            .ToListAsync(CancellationToken);
        var differences = await database.SqlQuery<string>(
            $"""
            WITH folded AS (
                SELECT configuration.oid, configuration.cfgname, language.oid AS language
                FROM pg_ts_config AS configuration
                JOIN pg_ts_config AS language
                    ON language.cfgname = replace(configuration.cfgname, '_folded', '')
                    AND language.cfgnamespace = 'pg_catalog'::regnamespace
                WHERE configuration.cfgnamespace = 'catalog'::regnamespace),
            mapping AS (
                SELECT map.mapcfg, map.maptokentype, array_agg(dictionary.dictname::text ORDER BY map.mapseqno) AS dictionaries
                FROM pg_ts_config_map AS map
                JOIN pg_ts_dict AS dictionary ON dictionary.oid = map.mapdict
                GROUP BY map.mapcfg, map.maptokentype)
            SELECT folded.cfgname || ' ' || token.alias AS "Value"
            FROM folded
            CROSS JOIN ts_token_type('default') AS token
            LEFT JOIN mapping AS ours ON ours.mapcfg = folded.oid AND ours.maptokentype = token.tokid
            LEFT JOIN mapping AS theirs ON theirs.mapcfg = folded.language AND theirs.maptokentype = token.tokid
            WHERE ours.dictionaries IS DISTINCT FROM CASE
                WHEN token.alias IN ('word', 'hword', 'hword_part', 'numword', 'numhword', 'hword_numpart')
                    THEN ARRAY['unaccent'] || theirs.dictionaries
                ELSE theirs.dictionaries
            END
            """)
            .ToListAsync(CancellationToken);

        Assert.Equal(SearchConfigurations.AllFolded, folded.Order(StringComparer.Ordinal));
        Assert.Empty(differences);
    }

    private async Task<ProductPage> SearchAsync(FurnitureStore furniture, string terms)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);

        return await shopper.GetJsonAsync<ProductPage>($"/api/storefront/products?q={Uri.EscapeDataString(terms)}");
    }

    private async Task RenameAsync(FurnitureStore furniture, string slug, string name)
    {
        var listing = await ListingAsync(furniture, slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { Name = name, listing.Slug, listing.Description, listing.Price, listing.VatRate, listing.IsVisible, listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task DescribeAsync(FurnitureStore furniture, string slug, string description, int? sortOrder = null)
    {
        var listing = await ListingAsync(furniture, slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { listing.Name, listing.Slug, Description = description, listing.Price, listing.VatRate, listing.IsVisible, SortOrder = sortOrder ?? listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<ListingView> ListingAsync(FurnitureStore furniture, string slug)
    {
        var listings = await furniture.Admin.AdminListAsync<ListingView>($"/api/admin/stores/{furniture.Store.StoreId}/products", CancellationToken);

        return listings!.Single(candidate => candidate.Slug == slug);
    }

    private async Task ChangeCultureAsync(FurnitureStore furniture, string culture)
    {
        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}",
            new { furniture.Store.Name, Culture = culture, Theme = new { PrimaryColor = "#112233", SecondaryColor = "#FFFFFF", BorderRadius = 4 } },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private Task<List<string>> LoggedAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<string>($"SELECT terms AS \"Value\" FROM catalog.search_queries WHERE store_id = {furniture.Store.StoreId}")
            .ToListAsync(CancellationToken));

    private Task<int> CultureChangesAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM messaging.outbox_messages WHERE store_id = {furniture.Store.StoreId} AND type = 'store.culture-changed'")
            .SingleAsync(CancellationToken));

    private sealed record ProductPage(List<PagedItem> Items, int TotalCount);

    private sealed record PagedItem(string Slug, string Name);

    private sealed record ListingView(Guid Id, string Name, string Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder);
}
