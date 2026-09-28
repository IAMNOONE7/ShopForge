using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Privacy;

public sealed class CustomerDataTests(ShopForgeApiFactory factory)
{
    private const string Password = "Shop-forge-2026";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_export_holds_the_account_the_orders_and_everything_written()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (shopper, email, order) = await BuyerAsync(furniture);
        using var wished = await shopper.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = furniture.Products["walnut-chair"] });
        using var reviewed = await shopper.PostAsync("/api/storefront/products/oak-chair/reviews", new { Rating = 5, Text = "Sturdy and handsome." });

        var export = await ExportAsync(shopper);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.Accepted), (wished.StatusCode, reviewed.StatusCode));
        Assert.Equal(email, export.GetProperty("sections").GetProperty("account")[0].GetProperty("email").GetString());
        Assert.Equal("Rea", export.GetProperty("sections").GetProperty("account")[0].GetProperty("firstName").GetString());
        Assert.Equal(order.Number, export.GetProperty("sections").GetProperty("orders")[0].GetProperty("number").GetString());
        Assert.Equal("Alex Buyer", export.GetProperty("sections").GetProperty("orders")[0].GetProperty("billingAddress").GetProperty("fullName").GetString());
        Assert.Equal("Sturdy and handsome.", export.GetProperty("sections").GetProperty("reviews")[0].GetProperty("text").GetString());
        Assert.Single(export.GetProperty("sections").GetProperty("wishlist").EnumerateArray());
        Assert.Single(export.GetProperty("sections").GetProperty("invoices").EnumerateArray());

        shopper.Dispose();
    }

    // The point of the whole stage: the books still add up and the person is gone.
    [Fact]
    public async Task Erasing_leaves_the_books_adding_up_and_the_person_gone()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (shopper, email, order) = await BuyerAsync(furniture);
        var before = await AccountingAsync(furniture, order.Number);

        using var erased = await shopper.PostAsync("/api/storefront/account/delete", new { Password });
        var after = await AccountingAsync(furniture, order.Number);
        using var signsIn = await SignInAsync(furniture, email);
        using var theirSession = await shopper.GetAsync("/api/storefront/account/me");

        Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.VatRates, after.VatRates);
        Assert.Equal(before.InvoiceNumber, after.InvoiceNumber);
        Assert.Equal(before.Lines, after.Lines);
        Assert.Equal("(erased)", after.OrderEmail);
        Assert.Equal("(erased)", after.BillingName);
        Assert.Equal("(erased)", after.InvoiceBuyerName);
        Assert.Equal("(erased)", after.InvoiceBuyerEmail);
        Assert.Equal(HttpStatusCode.Unauthorized, signsIn.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, theirSession.StatusCode);

        shopper.Dispose();
    }

    [Fact]
    public async Task An_erased_customer_cannot_be_found_through_orders_reviews_or_returns()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (shopper, email, order) = await BuyerAsync(furniture);
        using var reviewed = await shopper.PostAsync("/api/storefront/products/oak-chair/reviews", new { Rating = 4, Text = "Good chair." });
        using var returned = await shopper.PostAsync(
            $"/api/storefront/account/orders/{order.Number}/returns",
            new { Lines = new[] { new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 } }, Reason = "My flat is too small." });
        using var wished = await shopper.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = furniture.Products["oak-bench"] });

        using var erased = await shopper.PostAsync("/api/storefront/account/delete", new { Password });

        var traces = await TracesOfAsync(furniture, email);

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.OK, HttpStatusCode.NoContent), (reviewed.StatusCode, returned.StatusCode, wished.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);
        Assert.Equal(0, traces.Orders);
        Assert.Equal(0, traces.Invoices);
        Assert.Equal(0, traces.Identities);
        Assert.Equal(0, traces.Customers);
        Assert.Equal(0, traces.Reviews);
        Assert.Equal(0, traces.Wishlist);
        Assert.Equal(0, traces.ReturnsWithACustomer);
        Assert.Equal(0, traces.ReturnReasons);

        // The return itself stays: it is the store's record of goods coming back.
        Assert.Equal(1, traces.Returns);

        shopper.Dispose();
    }

    [Fact]
    public async Task Taking_a_review_away_leaves_the_listing_s_rating_right()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (leaving, _, _) = await BuyerAsync(furniture, "walnut-chair");
        var (staying, _, _) = await BuyerAsync(furniture, "walnut-chair");
        await PublishedReviewAsync(furniture, leaving, "walnut-chair", rating: 2);
        await PublishedReviewAsync(furniture, staying, "walnut-chair", rating: 4);
        var withBoth = await ListingAsync(furniture, "walnut-chair");

        using var erased = await leaving.PostAsync("/api/storefront/account/delete", new { Password });
        var withOne = await ListingAsync(furniture, "walnut-chair");

        Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);
        Assert.Equal((3m, 2), (withBoth.Rating, withBoth.ReviewCount));
        Assert.Equal((4m, 1), (withOne.Rating, withOne.ReviewCount));

        leaving.Dispose();
        staying.Dispose();
    }

    // Erasing is a store's business, like everything else about a customer (D-102).
    [Fact]
    public async Task Erasing_at_one_store_leaves_the_same_person_at_another_alone()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        using var here = await RegisteredAsync(furniture.Store, email);
        using var there = await RegisteredAsync(furniture.OtherStore, email, "Another-store-2026");

        using var erased = await here.PostAsync("/api/storefront/account/delete", new { Password });

        using var hereAgain = await SignInAsync(furniture, email);
        using var thereStill = await SignInAsync(furniture, email, "Another-store-2026", furniture.OtherStore);

        Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, hereAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, thereStill.StatusCode);
    }

    [Fact]
    public async Task Getting_your_data_or_removing_it_needs_an_account_and_a_password()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (shopper, email, _) = await BuyerAsync(furniture);
        using var guest = new StorefrontApi(factory, furniture.Store);

        using var byAGuest = await guest.GetAsync("/api/storefront/account/export");
        using var deleteByAGuest = await guest.PostAsync("/api/storefront/account/delete", new { Password });
        using var guessed = await shopper.PostAsync("/api/storefront/account/delete", new { Password = "Not-their-password-1" });
        using var stillThere = await SignInAsync(furniture, email);

        Assert.Equal(HttpStatusCode.Unauthorized, byAGuest.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deleteByAGuest.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);

        shopper.Dispose();
    }

    // An entry that outlives the erasure must not undo it by naming them (D-117).
    [Fact]
    public async Task Both_requests_are_on_the_record_without_naming_anybody()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var (shopper, email, _) = await BuyerAsync(furniture);
        await ExportAsync(shopper);
        using var erased = await shopper.PostAsync("/api/storefront/account/delete", new { Password });

        var entries = await furniture.Admin.GetFromJsonAsync<List<AuditView>>("/api/admin/audit?pageSize=100", CancellationToken);
        var mine = entries!.Where(entry => entry.Action is "customer.exported" or "customer.erased").ToList();

        Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);
        Assert.Contains(mine, entry => entry.Action == "customer.exported");
        Assert.Contains(mine, entry => entry.Action == "customer.erased");
        Assert.All(mine, entry => Assert.DoesNotContain(email, entry.Subject, StringComparison.OrdinalIgnoreCase));
        Assert.All(mine, entry => Assert.Equal("Customer", entry.ActorKind));

        shopper.Dispose();
    }

    private async Task<JsonElement> ExportAsync(StorefrontApi shopper)
    {
        using var response = await shopper.GetAsync("/api/storefront/account/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement;
    }

    private async Task<HttpResponseMessage> SignInAsync(FurnitureStore furniture, string email, string password = Password, TestStore? store = null)
    {
        using var client = new StorefrontApi(factory, store ?? furniture.Store);

        return await client.PostAsync("/api/storefront/account/login", new { Email = email, Password = password });
    }

    private async Task<StorefrontApi> RegisteredAsync(TestStore store, string email, string password = Password)
    {
        var shopper = new StorefrontApi(factory, store);
        using var registered = await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = password, FirstName = "Rea", LastName = "Viewer", Phone = "+353 1 234 5678" });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.LatestLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await shopper.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return shopper;
    }

    private async Task<(StorefrontApi Shopper, string Email, PlacedOrder Order)> BuyerAsync(FurnitureStore furniture, string product = "oak-chair")
    {
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        var shopper = await RegisteredAsync(furniture.Store, email);

        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products[product], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        // The invoice is issued by a handler, so it exists once the outbox has run.
        await factory.EventuallyAsync(
            () => InvoiceCountAsync(furniture, order.Number),
            count => count > 0,
            CancellationToken);

        return (shopper, email, order);
    }

    private async Task PublishedReviewAsync(FurnitureStore furniture, StorefrontApi shopper, string slug, int rating)
    {
        using var written = await shopper.PostAsync($"/api/storefront/products/{slug}/reviews", new { Rating = rating, Text = "A review." });
        Assert.Equal(HttpStatusCode.Accepted, written.StatusCode);

        var pending = await furniture.Admin.GetFromJsonAsync<List<PendingReview>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/reviews?status=Pending", CancellationToken);

        foreach (var review in pending!.Where(review => review.Rating == rating))
        {
            using var published = await furniture.Admin.PostAsync(
                $"/api/admin/stores/{furniture.Store.StoreId}/reviews/{review.Id}/publish", null, CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        }
    }

    private Task<int> InvoiceCountAsync(FurnitureStore furniture, string orderNumber) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM orders.invoices WHERE store_id = {furniture.Store.StoreId} AND order_number = {orderNumber}")
            .SingleAsync(CancellationToken));

    private async Task<ListingView> ListingAsync(FurnitureStore furniture, string slug) =>
        (await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<ListingView>($"""
                SELECT rating_average AS rating, rating_count AS review_count
                FROM catalog.store_products
                WHERE store_id = {furniture.Store.StoreId} AND slug = {slug}
                """)
            .ToListAsync(CancellationToken))).Single();

    private async Task<AccountingView> AccountingAsync(FurnitureStore furniture, string orderNumber)
    {
        // Columns are aliased the way the rest of the schema is named, because that is what the mapping looks
        // for; the row is picked in C# rather than composed onto, which a raw query does not survive.
        var order = (await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<OrderView>($"""
                SELECT email, billing_full_name AS billing_name
                FROM orders.orders
                WHERE store_id = {furniture.Store.StoreId} AND number = {orderNumber}
                """)
            .ToListAsync(CancellationToken))).Single();

        var invoice = (await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<InvoiceView>($"""
                SELECT number, buyer_full_name AS buyer_name, buyer_email
                FROM orders.invoices
                WHERE store_id = {furniture.Store.StoreId} AND order_number = {orderNumber} AND kind = 'Invoice'
                """)
            .ToListAsync(CancellationToken))).Single();

        var totals = (await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<TotalsView>($"""
                SELECT count(*)::int AS lines,
                       coalesce(sum(l.quantity * l.unit_price - l.discount), 0) AS total,
                       coalesce(sum(l.vat_rate), 0) AS vat_rates
                FROM orders.invoice_lines l
                JOIN orders.invoices i ON i.id = l.invoice_id
                WHERE i.store_id = {furniture.Store.StoreId} AND i.order_number = {orderNumber} AND i.kind = 'Invoice'
                """)
            .ToListAsync(CancellationToken))).Single();

        return new AccountingView(
            totals.Total,
            totals.VatRates,
            order.Email,
            order.BillingName,
            totals.Lines,
            invoice.Number,
            invoice.BuyerName,
            invoice.BuyerEmail);
    }

    private async Task<TracesView> TracesOfAsync(FurnitureStore furniture, string email)
    {
        var store = furniture.Store.StoreId;

        return new TracesView(
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM orders.orders WHERE email = {email}"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM orders.invoices WHERE buyer_email = {email}"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM customers.customer_identities WHERE email = {email}"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM customers.store_customers sc JOIN customers.customer_identities ci ON ci.id = sc.customer_identity_id WHERE ci.email = {email}"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM catalog.product_reviews WHERE store_id = {store}"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM customers.wishlist_items WHERE store_id = {store}"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM orders.order_returns WHERE store_id = {store}"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM orders.order_returns WHERE store_id = {store} AND store_customer_id IS NOT NULL"),
            await CountAsync(furniture, $"SELECT count(*)::int AS \"Value\" FROM orders.order_returns WHERE store_id = {store} AND reason IS NOT NULL"));
    }

    private Task<int> CountAsync(FurnitureStore furniture, FormattableString sql) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database.SqlQuery<int>(sql).SingleAsync(CancellationToken));

    private sealed record ListingView(decimal Rating, int ReviewCount);

    private sealed record AccountingView(
        decimal Total,
        decimal VatRates,
        string OrderEmail,
        string BillingName,
        int Lines,
        string InvoiceNumber,
        string InvoiceBuyerName,
        string InvoiceBuyerEmail);

    private sealed record OrderView(string Email, string BillingName);

    private sealed record InvoiceView(string Number, string BuyerName, string BuyerEmail);

    private sealed record TotalsView(int Lines, decimal Total, decimal VatRates);

    private sealed record TracesView(
        int Orders,
        int Invoices,
        int Identities,
        int Customers,
        int Reviews,
        int Wishlist,
        int Returns,
        int ReturnsWithACustomer,
        int ReturnReasons);

    private sealed record PendingReview(Guid Id, int Rating);

    private sealed record AuditView(string Action, string Subject, string ActorKind);
}
