using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Customers;

public sealed class AccountTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registering_and_verifying_creates_an_account_and_signs_the_customer_in()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var registered = await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        var verified = await VerifyAsync(shopper, email);
        var profile = await shopper.GetJsonAsync<CustomerView>("/api/storefront/account/me");

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal((email, "Ada", "Lovelace"), (verified.Email, verified.FirstName, verified.LastName));
        Assert.Equal(email, profile.Email);
    }

    [Fact]
    public async Task Signing_in_is_refused_until_the_address_is_verified()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var registered = await shopper.PostAsync("/api/storefront/account/register", Registration(email));

        using var beforeVerifying = await shopper.PostAsync("/api/storefront/account/login", new { Email = email, Password = Password });
        await VerifyAsync(shopper, email);
        using var signedOut = await shopper.PostAsync("/api/storefront/account/logout", null);
        using var afterVerifying = await shopper.PostAsync("/api/storefront/account/login", new { Email = email, Password = Password });

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, beforeVerifying.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterVerifying.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
    }

    [Fact]
    public async Task Registering_a_known_address_answers_the_same_and_adds_no_second_account()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var first = await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);

        using var second = await shopper.PostAsync("/api/storefront/account/register", Registration(email, firstName: "Someone"));
        var subjects = factory.Emails.For(email).Select(message => message.Subject).ToList();
        var profile = await shopper.GetJsonAsync<CustomerView>("/api/storefront/account/me");

        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal("Ada", profile.FirstName);
        Assert.Contains(subjects, subject => subject.Contains("already have", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_account_of_one_store_does_not_work_on_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var registered = await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);

        using var otherStore = new StorefrontApi(factory, furniture.OtherStore);
        using var login = await otherStore.PostAsync("/api/storefront/account/login", new { Email = email, Password = Password });
        using var profile = await otherStore.GetAsync("/api/storefront/account/me");

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    public async Task Verifying_claims_the_orders_placed_as_a_guest_with_that_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var guest = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(guest, furniture.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(guest, email);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);
        var orders = await shopper.GetJsonAsync<List<CustomerOrderView>>("/api/storefront/account/orders");

        Assert.Equal([order.Number], orders.Select(item => item.Number));
    }

    [Fact]
    public async Task An_order_placed_while_signed_in_shows_up_in_the_history()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);

        await AddToCartAsync(shopper, furniture.Products["walnut-chair"], 2);
        var order = await PlaceOrderAsync(shopper, email);
        var orders = await shopper.GetJsonAsync<List<CustomerOrderView>>("/api/storefront/account/orders");
        var detail = await shopper.GetJsonAsync<OrderView>($"/api/storefront/account/orders/{order.Number}");

        Assert.Equal([order.Number], orders.Select(item => item.Number));
        Assert.Equal(2, detail.Lines.Single().Quantity);
    }

    [Fact]
    public async Task A_customer_only_sees_the_orders_of_their_own_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        await PlaceOrderAsync(shopper, email);

        var otherProduct = await furniture.Admin.CreateProductAsync();
        var otherListing = await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, otherProduct, "Lamp", 10m);
        await furniture.Admin.StockAsync(otherProduct, 5);
        using var otherShopper = new StorefrontApi(factory, furniture.OtherStore);
        await otherShopper.PostAsync("/api/storefront/account/register", Registration(email, firstName: "Grace"));
        await VerifyAsync(otherShopper, email);
        await AddToCartAsync(otherShopper, otherListing, 1);
        await PlaceOrderAsync(otherShopper, email);

        var first = await shopper.GetJsonAsync<List<CustomerOrderView>>("/api/storefront/account/orders");
        var second = await otherShopper.GetJsonAsync<List<CustomerOrderView>>("/api/storefront/account/orders");

        // Order numbers restart per store, so the totals are what tell the two histories apart.
        Assert.Equal(104.90m, Assert.Single(first).GrandTotal);
        Assert.Equal(14.90m, Assert.Single(second).GrandTotal);
    }

    [Fact]
    public async Task A_reset_link_replaces_the_password_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);

        using var asked = await shopper.PostAsync("/api/storefront/account/password/forgot", new { Email = email });
        var token = factory.Emails.LatestLinkFor(email);
        using var reset = await shopper.PostAsync("/api/storefront/account/password/reset", new { Token = token, Password = "New-password-2026" });
        using var reused = await shopper.PostAsync("/api/storefront/account/password/reset", new { Token = token, Password = "Another-password-2026" });
        using var oldPassword = await shopper.PostAsync("/api/storefront/account/login", new { Email = email, Password = Password });
        using var newPassword = await shopper.PostAsync("/api/storefront/account/login", new { Email = email, Password = "New-password-2026" });

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task Asking_to_reset_an_unknown_address_gives_nothing_away()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var response = await shopper.PostAsync("/api/storefront/account/password/forgot", new { Email = email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.For(email));
    }

    [Fact]
    public async Task The_profile_can_be_changed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);

        using var updated = await shopper.PutAsync(
            "/api/storefront/account/me",
            new { FirstName = "Ada", LastName = "Byron", Phone = "+353 1 234 5678" });
        var profile = await shopper.ReadAsync<CustomerView>(updated);

        Assert.Equal(("Byron", "+353 1 234 5678"), (profile.LastName, profile.Phone));
    }

    [Fact]
    public async Task A_cart_filled_before_signing_in_is_still_there_afterwards()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);
        using var loggedOut = await shopper.PostAsync("/api/storefront/account/logout", null);

        await AddToCartAsync(shopper, furniture.Products["beech-stool"], 2);
        using var login = await shopper.PostAsync("/api/storefront/account/login", new { Email = email, Password = Password });
        var cart = await shopper.GetJsonAsync<CartView>("/api/storefront/cart");

        Assert.Equal(HttpStatusCode.OK, loggedOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(2, cart.Count);
    }

    private const string Password = "Shop-forge-2026";

    private static string UniqueEmail() => $"buyer-{Guid.NewGuid():N}@example.test";

    private static object Registration(string email, string firstName = "Ada") =>
        new { Email = email, Password, FirstName = firstName, LastName = "Lovelace", Phone = (string?)null };

    private async Task<CustomerView> VerifyAsync(StorefrontApi shopper, string email)
    {
        var token = factory.Emails.LatestLinkFor(email);
        using var response = await shopper.PostAsync("/api/storefront/account/verify", new { Token = token });

        return await shopper.ReadAsync<CustomerView>(response);
    }

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrder> PlaceOrderAsync(StorefrontApi shopper, string email)
    {
        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));

        return await shopper.ReadAsync<PlacedOrder>(response, HttpStatusCode.Created);
    }

    private sealed record CustomerView(string Email, string FirstName, string LastName, string? Phone);

    private sealed record CustomerOrderView(string Number, string Status, decimal GrandTotal, int Items);
}
