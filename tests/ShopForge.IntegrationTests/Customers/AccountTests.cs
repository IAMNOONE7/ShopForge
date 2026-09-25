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
        var subjects = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.For(email).Select(message => message.Subject).ToList()),
            messages => messages.Any(subject => subject.Contains("already have", StringComparison.Ordinal)),
            CancellationToken);
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
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.LatestLinkFor(email)),
            link => link is not null,
            CancellationToken);
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

    // The address on a signed-in customer's order is the one they proved, not one typed into the form: the shop
    // cannot be asked to write to somebody else (D-101).
    [Fact]
    public async Task An_order_of_a_signed_in_customer_goes_to_the_address_of_the_account()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        var order = await PlaceOrderAsync(shopper, email: "somebody.else@example.test");
        var placed = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var orders = await shopper.GetJsonAsync<List<CustomerOrderView>>("/api/storefront/account/orders");

        Assert.Equal(email, placed.Email);
        Assert.Contains(orders, own => own.Number == order.Number);
        Assert.Empty(factory.Emails.For("somebody.else@example.test"));
    }

    // Two stores of one company are two shops to the customer: the same address registers at each of them with its
    // own password, and neither store's password is any use at the other (D-102).
    [Fact]
    public async Task The_same_address_registers_again_at_another_store_with_its_own_password()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        const string otherPassword = "Other-store-2026";
        using var first = new StorefrontApi(factory, furniture.Store);
        using var second = new StorefrontApi(factory, furniture.OtherStore);
        await first.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(first, email);

        using var registered = await second.PostAsync("/api/storefront/account/register", Registration(email, password: otherPassword));
        await VerifyAsync(second, email);

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(furniture.Store, email, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(furniture.Store, email, otherPassword));
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(furniture.OtherStore, email, otherPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(furniture.OtherStore, email, Password));
    }

    // Anyone can type somebody else's address into a registration form. Doing so at another store must leave the
    // account they already have exactly as it was, whether or not the link is ever used.
    [Fact]
    public async Task Registering_somebody_else_s_address_at_another_store_changes_nothing_of_theirs()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var owner = new StorefrontApi(factory, furniture.Store);
        using var stranger = new StorefrontApi(factory, furniture.OtherStore);
        await owner.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(owner, email);

        using var registered = await stranger.PostAsync("/api/storefront/account/register", Registration(email, password: "Chosen-by-a-stranger-2026"));

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(furniture.Store, email, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(furniture.Store, email, "Chosen-by-a-stranger-2026"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(furniture.OtherStore, email, "Chosen-by-a-stranger-2026"));
    }

    [Fact]
    public async Task A_verification_link_of_one_store_does_not_work_at_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var other = new StorefrontApi(factory, furniture.OtherStore);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        var token = await LinkAsync(email);

        using var elsewhere = await other.PostAsync("/api/storefront/account/verify", new { Token = token });
        using var here = await shopper.PostAsync("/api/storefront/account/verify", new { Token = token });

        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.OK, here.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(furniture.OtherStore, email, Password));
    }

    [Fact]
    public async Task A_reset_link_of_one_store_does_not_work_at_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        const string otherPassword = "Other-store-2026";
        using var first = new StorefrontApi(factory, furniture.Store);
        using var second = new StorefrontApi(factory, furniture.OtherStore);
        await first.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(first, email);
        await second.PostAsync("/api/storefront/account/register", Registration(email, password: otherPassword));
        await VerifyAsync(second, email);

        await first.PostAsync("/api/storefront/account/password/forgot", new { Email = email });
        var token = await LinkAsync(email);
        using var elsewhere = await second.PostAsync("/api/storefront/account/password/reset", new { Token = token, Password = "Taken-over-2026" });
        using var here = await first.PostAsync("/api/storefront/account/password/reset", new { Token = token, Password = "Chosen-again-2026" });

        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, here.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(furniture.Store, email, "Chosen-again-2026"));
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(furniture.OtherStore, email, otherPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(furniture.OtherStore, email, "Chosen-again-2026"));
    }

    // A shop the customer never registered with does not write to them, however well the company knows the address.
    [Fact]
    public async Task Asking_to_reset_at_a_store_without_an_account_sends_nothing()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var other = new StorefrontApi(factory, furniture.OtherStore);
        await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        await VerifyAsync(shopper, email);

        using var asked = await other.PostAsync("/api/storefront/account/password/forgot", new { Email = email });
        await factory.DispatchOutboxAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.DoesNotContain(factory.Emails.For(email), message => message.Subject.Contains("Reset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Registering_again_before_verifying_keeps_the_newest_password()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = UniqueEmail();
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var first = await shopper.PostAsync("/api/storefront/account/register", Registration(email));
        using var second = await shopper.PostAsync("/api/storefront/account/register", Registration(email, password: "Second-attempt-2026"));
        await VerifyAsync(shopper, email);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(furniture.Store, email, "Second-attempt-2026"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(furniture.Store, email, Password));
    }

    private const string Password = "Shop-forge-2026";

    private static string UniqueEmail() => $"buyer-{Guid.NewGuid():N}@example.test";

    private static object Registration(string email, string firstName = "Ada", string password = Password) =>
        new { Email = email, Password = password, FirstName = firstName, LastName = "Lovelace", Phone = (string?)null };

    private async Task<HttpStatusCode> SignInAsync(TestStore store, string email, string password)
    {
        using var shopper = new StorefrontApi(factory, store);
        using var response = await shopper.PostAsync("/api/storefront/account/login", new { Email = email, Password = password });

        return response.StatusCode;
    }

    private Task<string?> LinkAsync(string email) =>
        factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.LatestLinkFor(email)),
            link => link is not null,
            CancellationToken);

    private readonly Dictionary<string, string> _linksUsed = [];

    private async Task<CustomerView> VerifyAsync(StorefrontApi shopper, string email)
    {
        // The link is in a message the outbox delivers. Where one address registers at two stores, the second link
        // takes a moment to arrive, and the first one is no use at the second store (D-102) — so wait for a new one.
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.LatestLinkFor(email)),
            link => link is not null && (!_linksUsed.TryGetValue(email, out var used) || link != used),
            CancellationToken);
        _linksUsed[email] = token!;

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
