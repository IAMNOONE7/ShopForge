using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Security;

// The other half of the sweep. The path sweep proves a store cannot name another store's row in a URL; it could
// not see a row named in a request body, so a bulk action or an assignment that forgot its tenancy would have
// passed it (D-191). Here the attacker acts on rows of their own in the path and names the victim's in the body.
//
// The assertion is not a status code, because these routes legitimately answer in different ways — a bulk
// action finds nothing and reports changing nothing, an assignment refuses. What must hold everywhere is that
// the victim's catalogue is exactly as it was, byte for byte, and that nothing of the attacker's now points at
// it. The database already refuses a cross-store reference within a store's catalogue — every one of those keys
// carries the store — so the exposures this exists for are the two no key prevents: a write to the victim's own
// rows, and a listing of another company's product, whose key carries no tenant.
public sealed class BodyIdSweepTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Store-scoped: the company's own admin, acting in its second store, names rows of its first.
    private static readonly (string Method, string Route, string Body)[] StoreScoped =
    [
        ("POST", "products/bulk/visibility", """{"storeProductIds":["{victimListing}"],"isVisible":false}"""),
        ("POST", "products/bulk/price", """{"storeProductIds":["{victimListing}"],"set":1}"""),
        ("POST", "products/bulk/categories", """{"storeProductIds":["{victimListing}"],"addCategoryIds":["{attackerCategory}"]}"""),
        ("POST", "products/bulk/archive", """{"storeProductIds":["{victimListing}"],"archived":true}"""),
        ("POST", "products/bulk/tax-class", """{"storeProductIds":["{victimListing}"],"taxClass":"{attackerTaxClass}"}"""),
        ("POST", "categories", """{"name":"Taken","slug":"taken-{unique}","sortOrder":0,"parentId":"{victimCategory}"}"""),
        ("PUT", "categories/{attackerCategory}", """{"name":"Taken","slug":"taken-{unique}","sortOrder":0,"parentId":"{victimCategory}"}"""),
        ("PUT", "categories/{attackerCategory}/attributes", """{"attributeIds":["{victimAttribute}"]}"""),
        ("PUT", "products/{attackerListing}/categories", """{"categoryIds":["{victimCategory}"]}"""),
        ("PUT", "attributes/{attackerAttribute}/options", """{"optionIds":["{victimOption}"]}"""),
    ];

    // Tenant-scoped: another company's admin, acting on rows of its own, names the first company's.
    private static readonly (string Method, string Route, string Body)[] TenantScoped =
    [
        ("POST", "stores/{strangerStore}/products", """{"productId":"{victimProduct}","name":"Taken","slug":"taken-{unique}","price":1,"vatRate":21,"isVisible":true,"sortOrder":0}"""),
        ("PUT", "products/{strangerProduct}/images", """{"imageIds":["{victimImage}"]}"""),
    ];

    public static TheoryData<string, string, string> StoreScopedRoutes() => Rows(StoreScoped);

    public static TheoryData<string, string, string> TenantScopedRoutes() => Rows(TenantScoped);

    [Theory]
    [MemberData(nameof(StoreScopedRoutes))]
    public async Task A_store_cannot_reach_a_sibling_store_s_rows_through_a_body(string method, string route, string body)
    {
        var world = await WorldAsync();
        var before = await FingerprintAsync(world);

        using var response = await SendAsync(world.Owner, method, $"/api/admin/stores/{world.AttackerStore}/{world.Fill(route)}", world.Fill(body));
        var after = await FingerprintAsync(world);

        Assert.True((int)response.StatusCode < 500, $"{method} {route} answered {(int)response.StatusCode}");
        Assert.Equal(before, after);
    }

    [Theory]
    [MemberData(nameof(TenantScopedRoutes))]
    public async Task A_company_cannot_reach_another_company_s_rows_through_a_body(string method, string route, string body)
    {
        var world = await WorldAsync();
        var before = await FingerprintAsync(world);

        using var response = await SendAsync(world.Stranger, method, $"/api/admin/{world.Fill(route)}", world.Fill(body));
        var after = await FingerprintAsync(world);

        Assert.True((int)response.StatusCode < 500, $"{method} {route} answered {(int)response.StatusCode}");
        Assert.Equal(before, after);

        // The one cross-company reference no key in the database refuses: a store listing a product of another
        // tenant. It changes nothing of the victim's, so the fingerprint cannot see it; this can.
        var listedTheirs = await factory.QueryAsync(world.Victim, dbContext => dbContext.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM catalog.store_products listing
                JOIN catalog.products product ON product.id = listing.product_id
                JOIN stores.stores store ON store.id = listing.store_id
                WHERE product.tenant_id <> store.tenant_id
                """)
            .SingleAsync(CancellationToken));
        Assert.Equal(0, listedTheirs);
    }

    // The list above is written by hand, so the one thing it cannot promise is that it is complete. This asks
    // the router which request bodies name a row — a property of type Guid, or a list of them, whose name ends
    // in Id or Ids — and requires every such route to be swept or excused.
    [Fact]
    public void Every_route_that_names_a_row_in_its_body_is_swept_or_excused()
    {
        var swept = StoreScoped.Select(route => Shape($"/api/admin/stores/{{storeId:guid}}/{route.Route}", route.Method))
            .Concat(TenantScoped.Select(route => Shape($"/api/admin/{route.Route}", route.Method)))
            .Concat(NotSwept.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var missing = BodyRoutesOfTheApplication()
            .Where(route => !swept.Contains(Shape(route.Route, route.Method)))
            .Select(route => $"{route.Method} {route.Route} ({route.Body})")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, $"Names a row in its body, not swept and not excused:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    // Routes that name a row in a body which another store or company cannot own, each with its reason.
    private static readonly Dictionary<string, string> NotSwept = new(StringComparer.Ordinal)
    {
        // Listing a product in a sibling store of the same company is the feature, not an attack: products
        // belong to the company and stores choose which to sell (SourceOfTruth §9). Swept tenant-wide above.
        ["POST /api/admin/stores/{}/products"] = "a company's own product, listed in any of its stores",
    };

    private List<(string Method, string Route, string Body)> BodyRoutesOfTheApplication()
    {
        using var started = factory.CreateClient();

        return
        [
            .. factory.Services.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/admin/", StringComparison.Ordinal) == true)
                .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>()
                    .Where(accepts => accepts.RequestType is { } type && NamesARow(type))
                    .SelectMany(accepts => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["POST"])
                        .Select(method => (Method: method, Route: endpoint.RoutePattern.RawText!.TrimEnd('/'), Body: accepts.RequestType!.Name))))
                .Distinct(),
        ];
    }

    private static bool NamesARow(Type body) =>
        body.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property =>
            (property.Name.EndsWith("Id", StringComparison.Ordinal) || property.Name.EndsWith("Ids", StringComparison.Ordinal))
            && IsGuidOrGuids(property.PropertyType));

    private static bool IsGuidOrGuids(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying == typeof(Guid)
        : type == typeof(Guid)
        || (type.IsGenericType && type.GetGenericArguments() is [var argument] && IsGuidOrGuids(argument));

    private static string Shape(string route, string method) =>
        $"{method} {System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]+\}", "{}").TrimEnd('/')}";

    // Everything of the victim's a body could reach, read straight from the tables so no filter can hide a
    // change: listings and what they cost and show, their categories, the category tree, which attributes a
    // category offers, the order of an attribute's options, and the order of the company's pictures.
    private Task<string> FingerprintAsync(SweepWorld world) =>
        factory.QueryAsync(world.Victim, dbContext => dbContext.Database
            .SqlQuery<string>($"""
                SELECT coalesce(md5(string_agg(line, '|' ORDER BY line)), '') AS "Value"
                FROM (
                    SELECT 'listing:' || id || ':' || price || ':' || is_visible || ':' || coalesce(archived_at::text, '') || ':' || coalesce(tax_class, '') || ':' || sort_order AS line
                    FROM catalog.store_products WHERE store_id = {world.Victim.StoreId}
                    UNION ALL SELECT 'in:' || store_product_id || ':' || category_id FROM catalog.product_categories WHERE store_id = {world.Victim.StoreId}
                    UNION ALL SELECT 'category:' || id || ':' || coalesce(parent_id::text, '') || ':' || coalesce(archived_at::text, '') || ':' || slug FROM catalog.categories WHERE store_id = {world.Victim.StoreId}
                    UNION ALL SELECT 'offers:' || category_id || ':' || attribute_definition_id || ':' || sort_order FROM catalog.category_attributes WHERE store_id = {world.Victim.StoreId}
                    UNION ALL SELECT 'option:' || id || ':' || sort_order FROM catalog.attribute_options WHERE store_id = {world.Victim.StoreId}
                    UNION ALL SELECT 'picture:' || image.id || ':' || image.product_id || ':' || image.position
                        FROM catalog.product_images image JOIN catalog.products product ON product.id = image.product_id
                        WHERE product.tenant_id = {world.Victim.TenantId}
                ) victim
                """)
            .SingleAsync(CancellationToken));

    private async Task<SweepWorld> WorldAsync()
    {
        var victim = await FurnitureStore.CreateAsync(factory);
        var stranger = await FurnitureStore.CreateAsync(factory);
        var victimStore = victim.Store.StoreId;
        var attackerStore = victim.OtherStore.StoreId;

        var attributes = await victim.Admin.GetFromJsonAsync<List<AttributeWithOptions>>($"/api/admin/stores/{victimStore}/attributes", CancellationToken);
        var withOptions = attributes!.First(attribute => attribute.Options.Count > 0);

        using var uploaded = await victim.Admin.UploadImageAsync(victim.ProductIds["oak-chair"], AdminCatalogApi.PngBytes);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var victimImage = (await uploaded.Content.ReadFromJsonAsync<Named>(CancellationToken))!.Id;

        // Rows the attacker owns in the sibling store, so the path is legitimately theirs.
        var attackerCategory = await victim.Admin.CreateCategoryAsync(attackerStore, "Theirs");
        var attackerListing = await victim.Admin.ListProductAsync(attackerStore, await victim.Admin.CreateProductAsync(), "Theirs", 10m);
        var attackerAttribute = await victim.Admin.CreateAttributeAsync(
            attackerStore, new { Name = $"Theirs {Guid.NewGuid():N}", Type = "select", IsFilterable = true, IsVisibleOnProductPage = true, Options = new[] { "One", "Two" } });

        const string AttackerTaxClass = "theirs";
        using var taxClass = await victim.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{attackerStore}/tax-classes", new { Name = "Theirs", Code = AttackerTaxClass }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, taxClass.StatusCode);

        return new SweepWorld(
            victim.Admin,
            stranger.Admin,
            victim.Store,
            attackerStore,
            stranger.Store.StoreId,
            stranger.ProductIds["oak-chair"],
            victim.Products["oak-chair"],
            victim.ProductIds["oak-chair"],
            victim.ChairsCategoryId,
            withOptions.Id,
            withOptions.Options[0].Id,
            victimImage,
            attackerCategory,
            attackerListing,
            attackerAttribute,
            AttackerTaxClass);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        return await client.SendAsync(request, CancellationToken);
    }

    private static TheoryData<string, string, string> Rows((string Method, string Route, string Body)[] routes)
    {
        var data = new TheoryData<string, string, string>();

        foreach (var (method, route, body) in routes)
        {
            data.Add(method, route, body);
        }

        return data;
    }

    private sealed record SweepWorld(
        HttpClient Owner,
        HttpClient Stranger,
        TestStore Victim,
        Guid AttackerStore,
        Guid StrangerStore,
        Guid StrangerProduct,
        Guid VictimListing,
        Guid VictimProduct,
        Guid VictimCategory,
        Guid VictimAttribute,
        Guid VictimOption,
        Guid VictimImage,
        Guid AttackerCategory,
        Guid AttackerListing,
        Guid AttackerAttribute,
        string AttackerTaxClass)
    {
        public string Fill(string text) => text
            .Replace("{victimListing}", VictimListing.ToString(), StringComparison.Ordinal)
            .Replace("{victimProduct}", VictimProduct.ToString(), StringComparison.Ordinal)
            .Replace("{victimCategory}", VictimCategory.ToString(), StringComparison.Ordinal)
            .Replace("{victimAttribute}", VictimAttribute.ToString(), StringComparison.Ordinal)
            .Replace("{victimOption}", VictimOption.ToString(), StringComparison.Ordinal)
            .Replace("{victimImage}", VictimImage.ToString(), StringComparison.Ordinal)
            .Replace("{attackerCategory}", AttackerCategory.ToString(), StringComparison.Ordinal)
            .Replace("{attackerListing}", AttackerListing.ToString(), StringComparison.Ordinal)
            .Replace("{attackerAttribute}", AttackerAttribute.ToString(), StringComparison.Ordinal)
            .Replace("{attackerTaxClass}", AttackerTaxClass, StringComparison.Ordinal)
            .Replace("{strangerStore}", StrangerStore.ToString(), StringComparison.Ordinal)
            .Replace("{strangerProduct}", StrangerProduct.ToString(), StringComparison.Ordinal)
            .Replace("{unique}", Guid.NewGuid().ToString("N")[..8], StringComparison.Ordinal);
    }

    private sealed record Named(Guid Id);

    private sealed record AttributeWithOptions(Guid Id, List<Named> Options);
}
