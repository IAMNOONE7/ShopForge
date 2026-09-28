using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Stores;

public sealed class StoreDomainTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_merchant_adds_a_domain_is_told_what_to_publish_and_the_store_answers_on_it()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await SignInAsync(store);
        var hostName = $"shop-{Guid.NewGuid():N}.example.test";

        var added = await AddAsync(admin, store, hostName);
        using var beforePublishing = await StorefrontAsync(hostName);
        using var tooEarly = await admin.PostAsync($"{Domains(store)}/{added.Id}/verify", null, CancellationToken);

        // The merchant puts the record in DNS, and only then does the name start answering.
        factory.Dns.Publish(added.ChallengeName!, added.ChallengeValue!);
        using var verified = await admin.PostAsync($"{Domains(store)}/{added.Id}/verify", null, CancellationToken);
        var proved = await verified.Content.ReadFromJsonAsync<DomainView>(CancellationToken);
        using var afterPublishing = await StorefrontAsync(hostName);

        Assert.Equal($"_shopforge-challenge.{hostName}", added.ChallengeName);
        Assert.False(added.IsVerified);
        Assert.Equal(HttpStatusCode.NotFound, beforePublishing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.True(proved!.IsVerified);
        Assert.Null(proved.ChallengeValue);
        Assert.Equal(HttpStatusCode.OK, afterPublishing.StatusCode);
    }

    // The whole point of the challenge: saying a name is yours is not the same as owning it (D-124).
    [Fact]
    public async Task A_domain_with_the_wrong_record_is_not_proved_and_is_not_served()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await SignInAsync(store);
        var hostName = $"shop-{Guid.NewGuid():N}.example.test";
        var added = await AddAsync(admin, store, hostName);

        factory.Dns.Publish(added.ChallengeName!, "somebody-elses-token");
        using var refused = await admin.PostAsync($"{Domains(store)}/{added.Id}/verify", null, CancellationToken);
        using var stillClosed = await StorefrontAsync(hostName);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stillClosed.StatusCode);
    }

    [Fact]
    public async Task A_domain_one_store_holds_cannot_be_taken_by_another()
    {
        var (mine, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (theirs, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var me = await SignInAsync(mine);
        using var them = await SignInAsync(theirs);
        var hostName = $"shop-{Guid.NewGuid():N}.example.test";
        await AddAsync(me, mine, hostName);

        // Not even unproved: the name is spoken for the moment somebody asks for it.
        using var byAnotherTenant = await them.PostAsJsonAsync(Domains(theirs), new { HostName = hostName }, CancellationToken);
        using var byTheSameStoreAgain = await me.PostAsJsonAsync(Domains(mine), new { HostName = hostName }, CancellationToken);

        // Said in as many words, rather than left to the unique index to refuse as a bare conflict.
        Assert.Equal(HttpStatusCode.Conflict, byAnotherTenant.StatusCode);
        Assert.Contains("already taken", await byAnotherTenant.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, byTheSameStoreAgain.StatusCode);
    }

    [Fact]
    public async Task The_store_moves_house_and_the_old_address_keeps_working()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await SignInAsync(store);
        var hostName = $"shop-{Guid.NewGuid():N}.example.test";
        var added = await VerifiedAsync(admin, store, hostName);

        using var promoted = await admin.PostAsync($"{Domains(store)}/{added.Id}/primary", null, CancellationToken);
        var domains = await DomainsAsync(admin, store);
        using var onTheNewOne = await StorefrontAsync(hostName);
        using var onTheOldOne = await StorefrontAsync(store.HostName);

        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal(hostName, domains.Single(domain => domain.IsPrimary).HostName);
        Assert.Equal(HttpStatusCode.OK, onTheNewOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, onTheOldOne.StatusCode);
    }

    [Fact]
    public async Task A_domain_can_be_dropped_but_not_the_one_the_store_calls_home()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await SignInAsync(store);
        var hostName = $"shop-{Guid.NewGuid():N}.example.test";
        var added = await VerifiedAsync(admin, store, hostName);
        var primary = (await DomainsAsync(admin, store)).Single(domain => domain.IsPrimary);

        using var theOriginal = await admin.DeleteAsync($"{Domains(store)}/{primary.Id}", CancellationToken);
        using var theNewOne = await admin.DeleteAsync($"{Domains(store)}/{added.Id}", CancellationToken);
        using var afterwards = await StorefrontAsync(hostName);

        Assert.Equal(HttpStatusCode.Conflict, theOriginal.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, theNewOne.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
    }

    [Fact]
    public async Task An_unproved_domain_cannot_become_the_one_the_store_calls_home()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await SignInAsync(store);
        var added = await AddAsync(admin, store, $"shop-{Guid.NewGuid():N}.example.test");

        using var promoted = await admin.PostAsync($"{Domains(store)}/{added.Id}/primary", null, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, promoted.StatusCode);
    }

    [Fact]
    public async Task Domains_are_a_store_s_own_business()
    {
        var (mine, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (theirs, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var me = await SignInAsync(mine);
        using var warehouse = await TestUsers.LoginAsync(
            factory, await TestUsers.CreateAsync(factory.Services, mine.TenantId, ShopForge.Access.Domain.TenantRole.Warehouse));

        using var anotherTenantsStore = await me.GetAsync(Domains(theirs), CancellationToken);
        using var byAReadOnlyRole = await warehouse.GetAsync(Domains(mine), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, anotherTenantsStore.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byAReadOnlyRole.StatusCode);
    }

    private static string Domains(TestStore store) => $"/api/admin/stores/{store.StoreId}/domains";

    private async Task<HttpClient> SignInAsync(TestStore store) =>
        await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

    private async Task<DomainView> AddAsync(HttpClient admin, TestStore store, string hostName)
    {
        using var response = await admin.PostAsJsonAsync(Domains(store), new { HostName = hostName }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<DomainView>(CancellationToken))!;
    }

    private async Task<DomainView> VerifiedAsync(HttpClient admin, TestStore store, string hostName)
    {
        var added = await AddAsync(admin, store, hostName);
        factory.Dns.Publish(added.ChallengeName!, added.ChallengeValue!);

        using var verified = await admin.PostAsync($"{Domains(store)}/{added.Id}/verify", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return added;
    }

    private async Task<List<DomainView>> DomainsAsync(HttpClient admin, TestStore store) =>
        (await admin.GetFromJsonAsync<List<DomainView>>(Domains(store), CancellationToken))!;

    private async Task<HttpResponseMessage> StorefrontAsync(string hostName)
    {
        using var client = factory.CreateClient();

        return await client.GetAsync($"http://{hostName}/api/storefront/store", CancellationToken);
    }

    private sealed record DomainView(
        Guid Id,
        string HostName,
        bool IsPrimary,
        bool IsVerified,
        DateTimeOffset? VerifiedAt,
        string? ChallengeName,
        string? ChallengeValue);
}
