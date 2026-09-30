using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Auditing;

public sealed class AuditTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_a_store_does_to_money_stock_and_prices_is_on_the_record()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PlaceAndPayAsync(furniture, shopper);
        var listing = furniture.Products["oak-chair"];

        using var priced = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing}",
            Listing(price: 777m),
            CancellationToken);
        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        using var stocked = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stock/{furniture.VariantIds["oak-chair"]}", new { Quantity = 3 }, CancellationToken);

        var entries = await AuditAsync(furniture.Admin);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (priced.StatusCode, refunded.StatusCode, stocked.StatusCode));
        Assert.Contains(entries, entry => entry.Action == "listing.price-changed" && entry.Details!.Contains("777", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Action == "order.refunded" && entry.Subject == order.Number);
        Assert.Contains(entries, entry => entry.Action == "stock.set" && entry.Details!.Contains("3", StringComparison.Ordinal));

        // Which store, and somebody rather than nobody (the name is pinned where the test knows it, below).
        var refund = entries.Single(entry => entry.Action == "order.refunded");
        Assert.Equal(("TenantUser", (Guid?)furniture.Store.StoreId), (refund.ActorKind, refund.StoreId));
        Assert.NotNull(refund.ActorId);
    }

    [Fact]
    public async Task Publishing_a_store_and_changing_who_works_here_are_on_the_record()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId);
        using var owner = await TestUsers.LoginAsync(factory, user);
        var colleague = await TestUsers.CreateAsync(factory.Services, store.TenantId, TenantRole.Support);
        var colleagues = await owner.GetFromJsonAsync<ColleaguesView>("/api/admin/users", CancellationToken);
        var id = colleagues!.Users.Single(candidate => candidate.Email == colleague.Email).Id;

        using var unpublished = await owner.PostAsync($"/api/admin/stores/{store.StoreId}/unpublish", null, CancellationToken);
        using var promoted = await owner.PutAsJsonAsync($"/api/admin/users/{id}/role", new { Role = "Admin" }, CancellationToken);
        using var turnedOff = await owner.PostAsync($"/api/admin/users/{id}/deactivate", null, CancellationToken);

        var entries = await AuditAsync(owner);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (unpublished.StatusCode, promoted.StatusCode, turnedOff.StatusCode));
        Assert.Contains(entries, entry => entry.Action == "store.unpublished");
        // Who did it, kept as it read at the time (D-116).
        var promotion = entries.Single(entry => entry.Action == "colleague.role-changed" && entry.Subject == colleague.Email);
        Assert.Equal(user.Email, promotion.ActorName);
        Assert.Contains("Support", promotion.Details!, StringComparison.Ordinal);
        Assert.Contains(entries, entry => entry.Action == "colleague.deactivated" && entry.Subject == colleague.Email);
    }

    // The record is the one thing that must never leak: it names people and what they did.
    [Fact]
    public async Task One_company_never_reads_another_company_s_record()
    {
        var mine = await FurnitureStore.CreateAsync(factory);
        var theirs = await FurnitureStore.CreateAsync(factory);
        using var theirShopper = new StorefrontApi(factory, theirs.Store);
        var theirOrder = await PlaceAndPayAsync(theirs, theirShopper);
        using var refunded = await theirs.Admin.PostAsync(
            $"/api/admin/stores/{theirs.Store.StoreId}/orders/{theirOrder.Number}/refund", null, CancellationToken);

        var mineSees = await AuditAsync(mine.Admin);
        var theySee = await AuditAsync(theirs.Admin);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Contains(theySee, entry => entry.Subject == theirOrder.Number);
        Assert.DoesNotContain(mineSees, entry => entry.Subject == theirOrder.Number);
        Assert.All(mineSees, entry => Assert.NotEqual(theirs.Store.StoreId, entry.StoreId));
    }

    [Fact]
    public async Task The_record_is_narrowed_by_store_by_action_and_by_page()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var elsewhere = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId));

        using var first = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/unpublish", null, CancellationToken);
        using var second = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products["oak-chair"]}",
            Listing(price: 555m),
            CancellationToken);

        var byAction = await AuditAsync(furniture.Admin, "?action=store.unpublished");
        var byOtherStore = await AuditAsync(furniture.Admin, $"?storeId={furniture.OtherStore.StoreId}");
        var onePerPage = await AuditAsync(furniture.Admin, "?pageSize=1");
        var secondPage = await AuditAsync(furniture.Admin, "?pageSize=1&page=2");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.NotEmpty(byAction);
        Assert.All(byAction, entry => Assert.Equal("store.unpublished", entry.Action));
        Assert.DoesNotContain(byOtherStore, entry => entry.StoreId == furniture.Store.StoreId);
        Assert.Single(onePerPage);
        Assert.Single(secondPage);
        Assert.NotEqual(onePerPage[0].Id, secondPage[0].Id);
        Assert.Equal(HttpStatusCode.OK, (await elsewhere.GetAsync("/api/admin/audit", CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task The_platform_sees_what_it_did_to_a_company_and_what_it_did_on_its_own()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);
        using var owner = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

        using var suspended = await operatorClient.PostAsync($"/api/platform/tenants/{store.TenantId}/suspend", null, CancellationToken);
        using var resumed = await operatorClient.PostAsync($"/api/platform/tenants/{store.TenantId}/resume", null, CancellationToken);
        var invited = $"operator-{Guid.NewGuid():N}@shopforge.test";
        using var invite = await operatorClient.PostAsJsonAsync("/api/platform/operators/invitations", new { Email = invited }, CancellationToken);

        var aboutTheTenant = await AuditAsync(operatorClient, path: $"/api/platform/tenants/{store.TenantId}/audit");
        var aboutItself = await AuditAsync(operatorClient, path: "/api/platform/audit");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (suspended.StatusCode, resumed.StatusCode, invite.StatusCode));
        Assert.Contains(aboutTheTenant, entry => entry.Action == "tenant.suspended" && entry.ActorKind == "PlatformUser");
        Assert.Contains(aboutTheTenant, entry => entry.Action == "tenant.resumed");

        // A platform action about no company is the platform's own business, and stays out of a company's record.
        Assert.Contains(aboutItself, entry => entry.Action == "operator.invited" && entry.Subject == invited);
        Assert.DoesNotContain(aboutTheTenant, entry => entry.Action == "operator.invited");

        // The company sees what was done to it, and nothing the platform did elsewhere.
        var theySee = await AuditAsync(owner);
        Assert.Contains(theySee, entry => entry.Action == "tenant.suspended");
        Assert.DoesNotContain(theySee, entry => entry.Action == "operator.invited");
    }

    [Fact]
    public async Task Reading_the_record_takes_more_than_being_signed_in()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var warehouse = await TestUsers.LoginAsync(
            factory, await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId, TenantRole.Warehouse));
        using var anonymous = factory.CreateClient();

        using var byWarehouse = await warehouse.GetAsync("/api/admin/audit", CancellationToken);
        using var byNobody = await anonymous.GetAsync("/api/admin/audit", CancellationToken);
        using var atThePlatform = await furniture.Admin.GetAsync("/api/platform/audit", CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, byWarehouse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, byNobody.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, atThePlatform.StatusCode);
    }

    // Append-only is not a convention somebody can forget: the save itself refuses (D-116).
    [Fact]
    public async Task An_entry_cannot_be_changed_or_removed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var unpublished = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/unpublish", null, CancellationToken);

        var changing = await factory.QueryAsync(furniture.Store, async dbContext =>
        {
            var entry = await dbContext.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM auditing.audit_entries WHERE store_id = {furniture.Store.StoreId} LIMIT 1")
                .SingleAsync(CancellationToken);

            return await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                var tracked = dbContext.ChangeTracker.Context.Model.FindEntityType("ShopForge.Infrastructure.Auditing.AuditEntry")!;
                var loaded = await dbContext.FindAsync(tracked.ClrType, [entry], CancellationToken);
                dbContext.Remove(loaded!);
                await dbContext.SaveChangesAsync(CancellationToken);
            });
        });

        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        Assert.Contains("never changed or removed", changing.Message, StringComparison.Ordinal);
    }

    private static object Listing(decimal price) => new
    {
        Name = "Oak chair",
        Slug = (string?)null,
        Description = "A chair.",
        Price = price,
        VatRate = 21m,
        IsVisible = true,
        SortOrder = 0,
    };

    private async Task<List<AuditView>> AuditAsync(HttpClient client, string query = "", string path = "/api/admin/audit")
    {
        var entries = await client.GetFromJsonAsync<List<AuditView>>($"{path}{query}", CancellationToken);

        return entries!;
    }

    private async Task<PlacedOrder> PlaceAndPayAsync(FurnitureStore furniture, StorefrontApi shopper)
    {
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        return order;
    }

    private sealed record AuditView(
        Guid Id,
        DateTimeOffset RecordedAt,
        string ActorKind,
        Guid? ActorId,
        string? ActorName,
        Guid? StoreId,
        string Action,
        string Subject,
        string? Details,
        string? IpAddress);

    private sealed record ColleaguesView(List<ColleagueView> Users);

    private sealed record ColleagueView(Guid Id, string Email, string Role, bool IsActive);
}
