using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Messaging;

public sealed class StoreMailTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_shop_s_mail_arrives_in_the_shop_s_colours_and_under_its_name()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";

        using var registered = await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Ada", LastName = "Lovelace", Phone = (string?)null });
        var message = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.For(email).FirstOrDefault()),
            delivered => delivered is not null,
            CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Contains(furniture.Store.Name, message!.Subject, StringComparison.Ordinal);
        Assert.NotNull(message.HtmlBody);
        Assert.Contains("background:#112233", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(furniture.Store.Name, message.HtmlBody, StringComparison.Ordinal);

        // The link is the point of the message, and it has to be clickable as well as readable.
        Assert.Contains("<a href=\"http", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("token=", message.Body, StringComparison.Ordinal);
    }

    // An invitation belongs to no shop, so it cannot borrow one's livery (D-111, D-121).
    [Fact]
    public async Task Mail_that_belongs_to_no_shop_goes_out_plain()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        var invited = $"colleague-{Guid.NewGuid():N}@example.test";

        using var invitation = await owner.PostAsJsonAsync(
            "/api/admin/users/invitations", new { Email = invited, Role = "Support" }, CancellationToken);
        var message = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.For(invited).FirstOrDefault()),
            delivered => delivered is not null,
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, invitation.StatusCode);
        Assert.NotNull(message!.HtmlBody);
        Assert.Contains("ShopForge", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("background:#112233", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain(store.Name, message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Money_in_a_message_is_written_the_way_the_store_s_customers_read_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        var confirmation = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.For(email).FirstOrDefault(message => message.Subject.Contains(order.Number, StringComparison.Ordinal))),
            delivered => delivered is not null,
            CancellationToken);

        // The store sells in euro to an Irish audience, so that is what the amount looks like.
        Assert.Contains("€", confirmation!.Body, StringComparison.Ordinal);
    }
}
