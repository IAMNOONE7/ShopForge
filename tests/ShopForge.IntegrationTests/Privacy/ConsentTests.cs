using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Privacy;

public sealed class ConsentTests(ShopForgeApiFactory factory)
{
    private const string Password = "Shop-forge-2026";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_customer_is_asked_nothing_until_they_answer_and_can_change_their_mind()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await CustomerAsync(furniture);

        var before = await ConsentsAsync(shopper);
        using var granted = await shopper.PutAsync("/api/storefront/account/consents/marketing", new { IsGranted = true });
        var afterGranting = await ConsentsAsync(shopper);
        using var withdrawn = await shopper.PutAsync("/api/storefront/account/consents/marketing", new { IsGranted = false });
        var afterWithdrawing = await ConsentsAsync(shopper);

        // Never answered reads as no, and says what the question was.
        Assert.Equal(("Marketing", false, (DateTimeOffset?)null), (before.Single().Purpose, before.Single().IsGranted, before.Single().DecidedAt));
        Assert.Contains("news and offers", before.Single().Statement, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.True(afterGranting.Single().IsGranted);
        Assert.NotNull(afterGranting.Single().DecidedAt);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.False(afterWithdrawing.Single().IsGranted);

        shopper.Dispose();
    }

    // What they agreed to has to be recoverable later, which is the only reason to keep the wording at all.
    [Fact]
    public async Task What_they_agreed_to_is_in_their_export_and_goes_when_they_do()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await CustomerAsync(furniture);
        using var granted = await shopper.PutAsync("/api/storefront/account/consents/marketing", new { IsGranted = true });

        using var exported = await shopper.GetAsync("/api/storefront/account/export");
        var export = await exported.Content.ReadFromJsonAsync<ExportView>(CancellationToken);
        var consent = export!.Sections["consents"].Single();

        using var erased = await shopper.PostAsync("/api/storefront/account/delete", new { Password });
        var left = await ConsentRowsAsync(furniture);

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(0, left);
        Assert.Equal(("Marketing", true), (consent.Purpose, consent.IsGranted));
        Assert.Contains("news and offers", consent.Statement, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);

        shopper.Dispose();
    }

    [Fact]
    public async Task Answering_is_something_only_the_customer_does_and_it_is_on_the_record()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await CustomerAsync(furniture);
        using var guest = new StorefrontApi(factory, furniture.Store);

        using var byAGuest = await guest.PutAsync("/api/storefront/account/consents/marketing", new { IsGranted = true });
        using var readByAGuest = await guest.GetAsync("/api/storefront/account/consents");
        using var granted = await shopper.PutAsync("/api/storefront/account/consents/marketing", new { IsGranted = true });

        var entries = await furniture.Admin.GetFromJsonAsync<List<AuditView>>("/api/admin/audit?action=consent.decided", CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, byAGuest.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, readByAGuest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.NotEmpty(entries!);
        Assert.All(entries!, entry => Assert.Contains("Marketing", entry.Details!, StringComparison.Ordinal));

        shopper.Dispose();
    }

    private Task<int> ConsentRowsAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM customers.customer_consents WHERE store_id = {furniture.Store.StoreId}")
            .SingleAsync(CancellationToken));

    private async Task<List<ConsentView>> ConsentsAsync(StorefrontApi shopper) =>
        await shopper.GetJsonAsync<List<ConsentView>>("/api/storefront/account/consents");

    private async Task<StorefrontApi> CustomerAsync(FurnitureStore furniture)
    {
        var email = $"shopper-{Guid.NewGuid():N}@example.test";
        var shopper = new StorefrontApi(factory, furniture.Store);

        using var registered = await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password, FirstName = "Ada", LastName = "Lovelace", Phone = (string?)null });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.LatestLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await shopper.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return shopper;
    }

    private sealed record ConsentView(string Purpose, bool IsGranted, string Statement, DateTimeOffset? DecidedAt);

    private sealed record ExportView(Dictionary<string, List<ConsentView>> Sections);

    private sealed record AuditView(string Action, string Subject, string? Details);
}
