using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Security;

// Every endpoint that takes an id, asked for somebody else's. Two shapes of attack, and the second is the one
// that is easy to get wrong: a company's own admin, legitimately signed in, naming a row that belongs to another
// of its stores. The tenant check passes there, and only the store filter stands between them (D-127).
public sealed partial class IdorSweepTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Store-scoped routes, called under a sibling store of the same company with the first store's ids.
    private static readonly (string Method, string Route, string? Body)[] StoreScoped =
    [
        ("PUT", "products/{storeProduct}", """{"name":"Taken","price":1,"vatRate":21,"isVisible":true,"sortOrder":0}"""),
        ("PUT", "products/{storeProduct}/categories", """{"categoryIds":[]}"""),
        ("GET", "products/{storeProduct}/attributes", null),
        ("PUT", "products/{storeProduct}/attributes", """{"values":{}}"""),
        ("PUT", "categories/{category}", """{"name":"Taken","slug":"taken","sortOrder":0}"""),
        ("PUT", "categories/{category}/attributes", """{"attributeIds":[]}"""),
        ("PUT", "feeds/{feed}/categories/{category}", """{"engineCategory":"Taken"}"""),
        ("PUT", "attributes/{attribute}", """{"name":"Taken","isFilterable":true,"isVisibleOnProductPage":true,"unit":null,"sortOrder":0}"""),
        ("PUT", "attributes/{attribute}/options", """{"optionIds":[]}"""),
        ("PUT", "attributes/{attribute}/options/{option}", """{"name":"Taken"}"""),
        ("DELETE", "attributes/{attribute}/options/{option}", null),
        ("DELETE", "attributes/{attribute}", null),
        ("POST", "attributes/{attribute}/options", """{"name":"Taken"}"""),
        ("POST", "reviews/{review}/publish", null),
        ("POST", "reviews/{review}/reject", null),
        ("POST", "returns/{return}/accept", null),
        ("POST", "returns/{return}/refuse", null),
        ("POST", "returns/{return}/receive", null),
        ("GET", "orders/{order}", null),
        ("GET", "orders/{order}/documents/{document}", null),
        ("POST", "orders/{order}/payment", null),
        ("POST", "orders/{order}/cancel", null),
        ("POST", "orders/{order}/refund", null),
        ("POST", "orders/{order}/shipment", """{"trackingNumber":"PKG-1"}"""),
        ("POST", "domains/{domain}/verify", null),
        ("POST", "domains/{domain}/primary", null),
        ("DELETE", "domains/{domain}", null),
        ("POST", "products/{storeProduct}/archive", null),
        ("POST", "products/{storeProduct}/restore", null),
        ("DELETE", "products/{storeProduct}", null),
        ("POST", "categories/{category}/archive", null),
        ("POST", "categories/{category}/restore", null),
        ("DELETE", "categories/{category}", null),
        ("PUT", "pages/{page}", """{"slug":"taken","title":"Taken","body":"Taken.","isPublished":true}"""),
        ("DELETE", "pages/{page}", null),
    ];

    // Tenant-scoped routes, called by another company's owner with the first company's ids.
    private static readonly (string Method, string Route, string? Body)[] TenantScoped =
    [
        ("PUT", "products/{product}", """{"sku":"TAKEN-1","ean":null,"weightGrams":null}"""),
        ("PUT", "stock/{variant}", """{"quantity":5}"""),
        ("GET", "stock/{variant}/movements", null),
        ("GET", "products/{product}/images/{image}", null),
        ("DELETE", "products/{product}/images/{image}", null),
        ("POST", "products/{product}/images", Image),
        ("PUT", "products/{product}/options", """{"names":[],"values":{}}"""),
        ("POST", "products/{product}/variants", """{"sku":"TAKEN-VARIANT","ean":null,"weightGrams":null,"optionValues":[]}"""),
        ("PUT", "products/{product}/variants/{variant}", """{"sku":"TAKEN-VARIANT","ean":null,"weightGrams":null,"optionValues":[]}"""),
        ("DELETE", "products/{product}/variants/{variant}", null),
        ("PUT", "users/{user}/role", """{"role":"Support"}"""),
        ("POST", "users/{user}/deactivate", null),
        ("POST", "users/{user}/activate", null),
        ("DELETE", "users/invitations/{invitation}", null),
    ];

    public static TheoryData<string, string, string?> StoreScopedRoutes() => Rows(StoreScoped);

    public static TheoryData<string, string, string?> TenantScopedRoutes() => Rows(TenantScoped);

    private static TheoryData<string, string, string?> Rows((string Method, string Route, string? Body)[] routes)
    {
        var data = new TheoryData<string, string, string?>();

        foreach (var (method, route, body) in routes)
        {
            data.Add(method, route, body);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StoreScopedRoutes))]
    public async Task One_store_of_a_company_cannot_reach_another_store_s_rows(string method, string route, string? body)
    {
        var world = await WorldAsync();
        var path = $"/api/admin/stores/{world.Neighbour}/{world.Fill(route)}";

        using var response = await SendAsync(world.Owner, method, path, body);

        // Not found rather than forbidden: a store is not told that somebody else's row exists (D-127).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TenantScopedRoutes))]
    public async Task One_company_cannot_reach_another_company_s_rows(string method, string route, string? body)
    {
        var world = await WorldAsync();
        var path = $"/api/admin/{world.Fill(route)}";

        using var response = await SendAsync(world.Stranger, method, path, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(StoreScopedRoutes))]
    public async Task Another_company_cannot_reach_a_store_at_all(string method, string route, string? body)
    {
        var world = await WorldAsync();
        var path = $"/api/admin/stores/{world.Store}/{world.Fill(route)}";

        using var response = await SendAsync(world.Stranger, method, path, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // The lists above are written by hand, so the one thing they cannot promise is that they are complete. This
    // asks the router instead: every admin route the application actually has, called by a stranger, must answer
    // "not found". Nothing needs adding to a list when an endpoint is written, because the store is resolved in
    // middleware and refuses before the handler is ever reached (D-132).
    [Fact]
    public async Task No_route_of_a_store_answers_a_stranger()
    {
        var world = await WorldAsync();
        var answered = new List<string>();

        foreach (var (method, route) in StoreRoutesOfTheApplication())
        {
            var path = route.Replace("{storeId:guid}", world.Store.ToString(), StringComparison.Ordinal);

            using var response = await SendAsync(world.Stranger, method, Placeholders(path), null);

            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                answered.Add($"{method} {route} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(answered.Count == 0, string.Join(Environment.NewLine, answered));
    }

    // And the matrix knows what it does not cover. A route keyed by something other than a row id cannot be
    // pointed at another store's data, and the reason is written down rather than left as an omission.
    [Fact]
    public void Every_route_that_names_a_row_is_either_swept_or_excused()
    {
        var swept = StoreScoped
            .Select(route => Shape($"/api/admin/stores/{{storeId:guid}}/{route.Route}"))
            .Concat(TenantScoped.Select(route => Shape($"/api/admin/{route.Route}")))
            .Concat(NotSwept.Keys.Select(Shape))
            .ToHashSet();

        var missing = AdminRoutesOfTheApplication()
            .Where(route => NamesARow(route.Route))
            .Select(route => Shape(route.Route))
            .Distinct()
            .Where(shape => !swept.Contains(shape))
            .Order()
            .ToList();

        Assert.True(missing.Count == 0, $"Not swept and not excused:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    // The same rows, asked for by the people who actually own them, so the sweep above is not passing because
    // every one of those routes happens to answer NotFound to everybody.
    [Fact]
    public async Task The_same_requests_are_answered_for_the_people_they_belong_to()
    {
        var world = await WorldAsync();

        using var listing = await SendAsync(world.Owner, "GET", $"/api/admin/stores/{world.Store}/products/{world.StoreProduct}/attributes", null);
        using var order = await SendAsync(world.Owner, "GET", $"/api/admin/stores/{world.Store}/orders/{world.Order}", null);
        using var movements = await SendAsync(world.Owner, "GET", $"/api/admin/stock/{world.Variant}/movements", null);
        using var review = await SendAsync(world.Owner, "POST", $"/api/admin/stores/{world.Store}/reviews/{world.Review}/publish", null);
        using var added = await world.Owner.PostAsJsonAsync(
            $"/api/admin/stores/{world.Store}/domains",
            new { HostName = $"spare-{Guid.NewGuid():N}.example.test" },
            CancellationToken);
        var spare = await added.Content.ReadFromJsonAsync<Named>(CancellationToken);
        using var domain = await SendAsync(world.Owner, "DELETE", $"/api/admin/stores/{world.Store}/domains/{spare!.Id}", null);

        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, order.StatusCode);
        Assert.Equal(HttpStatusCode.OK, movements.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, review.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, domain.StatusCode);
    }

    // The one swept route that takes a file rather than JSON; an empty body would be refused at binding, before
    // the handler ever looks at whose product it is.
    private const string Image = "(image)";

    // Routes whose id is not another store's to name, so pointing one store's URL at another store's row is not
    // a thing that can be attempted. Each one is here with the reason it is here.
    private static readonly Dictionary<string, string> NotSwept = new()
    {
        // The code belongs to whichever store the path names, so there is no second store's row in play.
        ["/api/admin/stores/{storeId:guid}/discounts/{code}"] = "keyed by a code of the store in the path",
        ["/api/admin/stores/{storeId:guid}/payment-methods/{code}"] = "keyed by a code of the store in the path",
        ["/api/admin/stores/{storeId:guid}/shipping-methods/{code}"] = "keyed by a code of the store in the path",
        ["/api/admin/stores/{storeId:guid}/pickup-points/{code}"] = "keyed by a code of the store in the path",

        // A feed is named by which shopping engine it is for, and that name means the same thing in every
        // store; the row behind it is found by the store in scope like every other (D-168).
        ["/api/admin/stores/{storeId:guid}/feeds/{feed}"] = "keyed by the name of a shopping engine",
        ["/api/admin/stores/{storeId:guid}/feeds/{feed}/token"] = "keyed by the name of a shopping engine",
        ["/api/admin/stores/{storeId:guid}/feeds/{feed}/run"] = "keyed by the name of a shopping engine",
        ["/api/admin/stores/{storeId:guid}/feeds/{feed}/check"] = "keyed by the name of a shopping engine",
        ["/api/admin/stores/{storeId:guid}/feeds/{feed}/categories"] = "keyed by the name of a shopping engine",

        // A dead letter has to be delivered and then fail before it can be named, which no fixture here
        // arranges; the query behind it is store-filtered like every other (D-123).
        ["/api/admin/stores/{storeId:guid}/failed-messages/{messageId:guid}/requeue"] = "needs a dead letter to exist",

        // A provider's key is the platform's, not another store's row: the path names which gateway, and the
        // connection it reaches is whichever one belongs to the store in the path (D-138).
        ["/api/admin/stores/{storeId:guid}/provider-connections/{provider}"] = "keyed by a provider of the platform",
        ["/api/admin/stores/{storeId:guid}/provider-connections/{provider}/secret"] = "keyed by a provider of the platform",

        // Suppression comes from a provider's webhook rather than from anything an admin can ask for.
        ["/api/admin/stores/{storeId:guid}/suppressed-addresses/{id:guid}"] = "needs a bounce to have arrived",
    };

    private static string Shape(string route) =>
        ParameterPattern().Replace(route, "{}").TrimEnd('/');

    // A route that names a row: one with a parameter of its own beyond the store it hangs off.
    private static bool NamesARow(string route) =>
        ParameterPattern().Matches(route).Count > (route.Contains("{storeId", StringComparison.Ordinal) ? 1 : 0);

    private static string Placeholders(string path) =>
        ParameterPattern().Replace(path, match => match.Value.Contains(":guid", StringComparison.Ordinal) ? $"{Guid.CreateVersion7()}" : "x");

    private IEnumerable<(string Method, string Route)> StoreRoutesOfTheApplication() =>
        AdminRoutesOfTheApplication().Where(route => route.Route.StartsWith("/api/admin/stores/{storeId", StringComparison.Ordinal));

    private List<(string Method, string Route)> AdminRoutesOfTheApplication()
    {
        // The server is built lazily, so the route table only exists once something has asked for a client.
        using var started = factory.CreateClient();

        return
        [
            .. factory.Services.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/admin/", StringComparison.Ordinal) == true)
                .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                    .Select(method => (Method: method, Route: endpoint.RoutePattern.RawText!.TrimEnd('/'))))
                .Distinct()
                .OrderBy(route => route.Route, StringComparer.Ordinal),
        ];
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex ParameterPattern();

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (body == Image)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(AdminCatalogApi.PngBytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "taken.png");
            form.Add(new StringContent("Taken"), "altText");
            request.Content = form;
        }
        else if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<World> WorldAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var stranger = await FurnitureStore.CreateAsync(factory);
        var storeId = furniture.Store.StoreId;

        var attributes = await furniture.Admin.GetFromJsonAsync<List<AttributeWithOptions>>(
            $"/api/admin/stores/{storeId}/attributes", CancellationToken);
        var domains = await furniture.Admin.GetFromJsonAsync<List<Named>>(
            $"/api/admin/stores/{storeId}/domains", CancellationToken);

        using var buyer = new StorefrontApi(factory, furniture.Store);
        var (orderNumber, reviewId, returnId) = await PurchaseAsync(furniture, buyer);

        return new World(
            furniture.Admin,
            stranger.Admin,
            storeId,
            furniture.OtherStore.StoreId,
            furniture.Products["oak-chair"],
            furniture.ProductIds["oak-chair"],
            furniture.ChairsCategoryId,
            WithOptions(attributes!).Id,
            reviewId,
            returnId,
            orderNumber,
            await VariantIdAsync(furniture),
            await InvoiceNumberAsync(furniture, orderNumber),
            await ImageIdAsync(furniture),
            domains!.Single().Id,
            await UserIdAsync(furniture),
            await InvitationIdAsync(furniture),
            await ContentPageIdAsync(furniture),
            WithOptions(attributes!).Options[0].Id);
    }

    private async Task<(string Order, Guid Review, Guid Return)> PurchaseAsync(FurnitureStore furniture, StorefrontApi buyer)
    {
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        using var registered = await buyer.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Ada", LastName = "Lovelace", Phone = (string?)null });
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await buyer.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        using var added = await buyer.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await buyer.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));
        var order = await buyer.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        using var written = await buyer.PostAsync("/api/storefront/products/oak-chair/reviews", new { Rating = 5, Text = "Good." });
        Assert.Equal(HttpStatusCode.Accepted, written.StatusCode);
        var reviews = await furniture.Admin.GetFromJsonAsync<List<Named>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/reviews?status=Pending", CancellationToken);

        using var requested = await buyer.PostAsync(
            $"/api/storefront/account/orders/{order.Number}/returns",
            new { Lines = new[] { new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 } }, Reason = "Too small." });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var returns = await furniture.Admin.GetFromJsonAsync<List<Named>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/returns", CancellationToken);

        return (order.Number, reviews!.Single().Id, returns!.Single().Id);
    }

    // The invoice is issued by the worker once the payment lands, so the sweep waits for the document it is
    // about to ask for as somebody else.
    private async Task<string> InvoiceNumberAsync(FurnitureStore furniture, string orderNumber)
    {
        var documents = await factory.EventuallyAsync(
            async () =>
            {
                var order = await furniture.Admin.GetFromJsonAsync<OrderDetail>(
                    $"/api/admin/stores/{furniture.Store.StoreId}/orders/{orderNumber}", CancellationToken);

                return order!.Documents;
            },
            found => found.Count > 0,
            CancellationToken);

        return documents[0].Number;
    }

    private async Task<Guid> VariantIdAsync(FurnitureStore furniture)
    {
        var products = await furniture.Admin.GetFromJsonAsync<List<ProductRow>>("/api/admin/products", CancellationToken);

        return products!.Single(product => product.Id == furniture.ProductIds["oak-chair"]).Variants[0].Id;
    }

    private async Task<Guid> ImageIdAsync(FurnitureStore furniture)
    {
        using var uploaded = await furniture.Admin.UploadImageAsync(furniture.ProductIds["oak-chair"], AdminCatalogApi.PngBytes);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);

        return (await uploaded.Content.ReadFromJsonAsync<Named>(CancellationToken))!.Id;
    }

    private async Task<Guid> UserIdAsync(FurnitureStore furniture)
    {
        var colleague = await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId, ShopForge.Access.Domain.TenantRole.Support);
        var colleagues = await furniture.Admin.GetFromJsonAsync<Colleagues>("/api/admin/users", CancellationToken);

        return colleagues!.Users.Single(user => user.Email == colleague.Email).Id;
    }

    private async Task<Guid> InvitationIdAsync(FurnitureStore furniture)
    {
        using var invited = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/users/invitations",
            new { Email = $"colleague-{Guid.NewGuid():N}@example.test", Role = "Support" },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);

        return (await invited.Content.ReadFromJsonAsync<Named>(CancellationToken))!.Id;
    }

    // The swept attribute has to be one with options, because two of the routes name one of them.
    private static AttributeWithOptions WithOptions(List<AttributeWithOptions> attributes) =>
        attributes.First(attribute => attribute.Options.Count > 0);

    private async Task<Guid> ContentPageIdAsync(FurnitureStore furniture)
    {
        using var created = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pages",
            new { Slug = "terms", Title = "Terms", Body = "Ours.", IsPublished = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await created.Content.ReadFromJsonAsync<Named>(CancellationToken))!.Id;
    }

    private sealed record World(
        HttpClient Owner,
        HttpClient Stranger,
        Guid Store,
        Guid Neighbour,
        Guid StoreProduct,
        Guid Product,
        Guid Category,
        Guid Attribute,
        Guid Review,
        Guid Return,
        string Order,
        Guid Variant,
        string Document,
        Guid Image,
        Guid Domain,
        Guid User,
        Guid Invitation,
        Guid ContentPage,
        Guid AttributeOption)
    {
        public string Fill(string route) => route
            .Replace("{storeProduct}", StoreProduct.ToString(), StringComparison.Ordinal)
            .Replace("{product}", Product.ToString(), StringComparison.Ordinal)
            .Replace("{category}", Category.ToString(), StringComparison.Ordinal)
            .Replace("{attribute}", Attribute.ToString(), StringComparison.Ordinal)
            .Replace("{review}", Review.ToString(), StringComparison.Ordinal)
            .Replace("{return}", Return.ToString(), StringComparison.Ordinal)
            .Replace("{variant}", Variant.ToString(), StringComparison.Ordinal)
            .Replace("{order}", Order, StringComparison.Ordinal)
            .Replace("{document}", Document, StringComparison.Ordinal)
            .Replace("{image}", Image.ToString(), StringComparison.Ordinal)
            .Replace("{feed}", "heureka", StringComparison.Ordinal)
            .Replace("{domain}", Domain.ToString(), StringComparison.Ordinal)
            .Replace("{user}", User.ToString(), StringComparison.Ordinal)
            .Replace("{invitation}", Invitation.ToString(), StringComparison.Ordinal)
            .Replace("{option}", AttributeOption.ToString(), StringComparison.Ordinal)
            .Replace("{page}", ContentPage.ToString(), StringComparison.Ordinal);
    }

    private sealed record AttributeWithOptions(Guid Id, List<Named> Options);

    private sealed record Named(Guid Id, string? Email);

    private sealed record ProductRow(Guid Id, List<Named> Variants);

    private sealed record OrderDetail(List<Document> Documents);

    private sealed record Document(string Number);

    private sealed record Colleagues(List<Named> Users);
}
