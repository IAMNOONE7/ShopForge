using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Security;

// Every endpoint that takes an id, asked for somebody else's. Two shapes of attack, and the second is the one
// that is easy to get wrong: a company's own admin, legitimately signed in, naming a row that belongs to another
// of its stores. The tenant check passes there, and only the store filter stands between them (D-127).
public sealed class IdorSweepTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Store-scoped routes, called under a sibling store of the same company with the first store's ids.
    public static TheoryData<string, string, string?> StoreScopedRoutes() => new()
    {
        { "PUT", "products/{storeProduct}", """{"name":"Taken","price":1,"vatRate":21,"isVisible":true,"sortOrder":0}""" },
        { "PUT", "products/{storeProduct}/categories", """{"categoryIds":[]}""" },
        { "GET", "products/{storeProduct}/attributes", null },
        { "PUT", "products/{storeProduct}/attributes", """{"values":{}}""" },
        { "PUT", "categories/{category}", """{"name":"Taken","slug":"taken","sortOrder":0}""" },
        { "PUT", "categories/{category}/attributes", """{"attributeIds":[]}""" },
        { "PUT", "attributes/{attribute}", """{"name":"Taken","isFilterable":true,"isVisibleOnProductPage":true,"unit":null,"sortOrder":0}""" },
        { "POST", "attributes/{attribute}/options", """{"name":"Taken"}""" },
        { "POST", "reviews/{review}/publish", null },
        { "POST", "reviews/{review}/reject", null },
        { "POST", "returns/{return}/accept", null },
        { "POST", "returns/{return}/refuse", null },
        { "POST", "returns/{return}/receive", null },
        { "GET", "orders/{order}", null },
        { "POST", "orders/{order}/payment", null },
        { "POST", "orders/{order}/cancel", null },
        { "POST", "orders/{order}/refund", null },
        { "POST", "orders/{order}/shipment", """{"trackingNumber":"PKG-1"}""" },
        { "POST", "domains/{domain}/verify", null },
        { "POST", "domains/{domain}/primary", null },
        { "DELETE", "domains/{domain}", null },
    };

    // Tenant-scoped routes, called by another company's owner with the first company's ids.
    public static TheoryData<string, string, string?> TenantScopedRoutes() => new()
    {
        { "PUT", "products/{product}", """{"sku":"TAKEN-1","ean":null,"weightGrams":null}""" },
        { "PUT", "stock/{product}", """{"quantity":5}""" },
        { "GET", "stock/{product}/movements", null },
        { "PUT", "users/{user}/role", """{"role":"Support"}""" },
        { "POST", "users/{user}/deactivate", null },
        { "POST", "users/{user}/activate", null },
        { "DELETE", "users/invitations/{invitation}", null },
    };

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

    // The same rows, asked for by the people who actually own them, so the sweep above is not passing because
    // every one of those routes happens to answer NotFound to everybody.
    [Fact]
    public async Task The_same_requests_are_answered_for_the_people_they_belong_to()
    {
        var world = await WorldAsync();

        using var listing = await SendAsync(world.Owner, "GET", $"/api/admin/stores/{world.Store}/products/{world.StoreProduct}/attributes", null);
        using var order = await SendAsync(world.Owner, "GET", $"/api/admin/stores/{world.Store}/orders/{world.Order}", null);
        using var movements = await SendAsync(world.Owner, "GET", $"/api/admin/stock/{world.Product}/movements", null);
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

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (body is not null)
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

        var attributes = await furniture.Admin.GetFromJsonAsync<List<Named>>(
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
            attributes![0].Id,
            reviewId,
            returnId,
            orderNumber,
            domains!.Single().Id,
            await UserIdAsync(furniture),
            await InvitationIdAsync(furniture));
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
        Guid Domain,
        Guid User,
        Guid Invitation)
    {
        public string Fill(string route) => route
            .Replace("{storeProduct}", StoreProduct.ToString(), StringComparison.Ordinal)
            .Replace("{product}", Product.ToString(), StringComparison.Ordinal)
            .Replace("{category}", Category.ToString(), StringComparison.Ordinal)
            .Replace("{attribute}", Attribute.ToString(), StringComparison.Ordinal)
            .Replace("{review}", Review.ToString(), StringComparison.Ordinal)
            .Replace("{return}", Return.ToString(), StringComparison.Ordinal)
            .Replace("{order}", Order, StringComparison.Ordinal)
            .Replace("{domain}", Domain.ToString(), StringComparison.Ordinal)
            .Replace("{user}", User.ToString(), StringComparison.Ordinal)
            .Replace("{invitation}", Invitation.ToString(), StringComparison.Ordinal);
    }

    private sealed record Named(Guid Id, string? Email);

    private sealed record Colleagues(List<Named> Users);
}
