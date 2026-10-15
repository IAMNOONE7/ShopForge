using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Caching;
using ShopForge.Infrastructure.Secrets;
using ShopForge.Shared.Caching;

namespace ShopForge.IntegrationTests.Replicas;

// Two replicas of the API over one database, which is what a deployment of more than one is: each with its own
// cache and its own listener, sharing nothing but PostgreSQL. What one changes, the other stops believing within
// the window D-202 states — two seconds — and a replica that lost the channel believes nothing it held before.
public sealed class ReplicaTests(ShopForgeApiFactory factory)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The abuse control V6 exists for: before D-202 the replica that did not make the change served the
    // suspended company's shops for up to five minutes.
    [Fact]
    public async Task Suspending_a_company_stops_the_other_replica_serving_its_shops_within_the_window()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var other = await ReplicaAsync();
        await ServedAsync(other, store.HostName);
        using var operatorClient = await TestPlatformUsers.SignInAsync(factory, CancellationToken);

        using var suspended = await operatorClient.PostAsync($"/api/platform/tenants/{store.TenantId}/suspend", null, CancellationToken);
        var took = await UntilNotServedAsync(other, store.HostName);

        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.True(took <= Window, $"the other replica served the suspended company's shop for {took.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task Removing_a_domain_stops_the_other_replica_serving_it_within_the_window()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var admin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        var hostName = $"shop-{Guid.NewGuid():N}.example.test";
        var domainId = await VerifiedDomainAsync(admin, store, hostName);
        using var other = await ReplicaAsync();
        await ServedAsync(other, hostName);

        using var removed = await admin.DeleteAsync($"/api/admin/stores/{store.StoreId}/domains/{domainId}", CancellationToken);
        var took = await UntilNotServedAsync(other, hostName);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.True(took <= Window, $"the other replica served the removed domain for {took.TotalMilliseconds:F0} ms");
    }

    // The channel's own failure. The company is suspended with nothing said, as if the word had been sent while the
    // listener was down: the replica must not come back believing what it held before the line dropped.
    [Fact]
    public async Task A_replica_whose_channel_dropped_believes_nothing_it_held_before()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var other = await ReplicaAsync();
        await ServedAsync(other, store.HostName);

        await CutOffAsync(other);
        await UntilAsync(() => !Cache(other).IsHearing, "the replica did not notice its channel had gone");
        await SuspendSayingNothingAsync(store);
        await UntilAsync(() => Cache(other).IsHearing, "the replica did not listen again");
        using var answer = await StorefrontAsync(other, store.HostName);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    // §4.3 item 7: a credential rotated on one replica stops being used on the other within the window, and one
    // revoked is gone from both (D-202).
    [Fact]
    public async Task A_secret_rotated_or_removed_on_one_replica_is_not_used_by_the_other_beyond_the_window()
    {
        var vault = new FakeVault();
        var name = $"store-{Guid.NewGuid():N}-comgate";
        using var first = await ReplicaAsync();
        using var second = await ReplicaAsync();
        var here = new KeyVaultSecretStore(vault, first.Services.GetRequiredService<IReplicaCache>());
        var there = new KeyVaultSecretStore(vault, second.Services.GetRequiredService<IReplicaCache>());
        await here.SetAsync(name, "first", CancellationToken);
        Assert.Equal("first", await there.FindAsync(name, CancellationToken));

        await here.SetAsync(name, "second", CancellationToken);
        var rotated = await TimedAsync(async () => await there.FindAsync(name, CancellationToken) == "second");
        await here.ForgetAsync(name, CancellationToken);
        var removed = await TimedAsync(async () => await there.FindAsync(name, CancellationToken) is null);

        Assert.True(rotated <= Window, $"the other replica used the old secret for {rotated.TotalMilliseconds:F0} ms");
        Assert.True(removed <= Window, $"the other replica used the removed secret for {removed.TotalMilliseconds:F0} ms");
    }

    // A second host over the same database, ready only once its listener is: until then it keeps nothing, and a test
    // of what it keeps would prove nothing.
    private async Task<WebApplicationFactory<Program>> ReplicaAsync()
    {
        var replica = factory.WithWebHostBuilder(_ => { });
        await UntilAsync(() => Cache(replica).IsHearing, "the replica's listener never started");

        return replica;
    }

    private static ReplicaCache Cache(WebApplicationFactory<Program> replica) => replica.Services.GetRequiredService<ReplicaCache>();

    // Served, and kept: the second request is answered from the replica's own copy.
    private async Task ServedAsync(WebApplicationFactory<Program> replica, string hostName)
    {
        using var first = await StorefrontAsync(replica, hostName);
        using var second = await StorefrontAsync(replica, hostName);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
    }

    // How long the replica went on serving the host. Asked every twenty milliseconds for well past the window, so a
    // failure says by how much it was missed.
    private async Task<TimeSpan> UntilNotServedAsync(WebApplicationFactory<Program> replica, string hostName) =>
        await TimedAsync(async () =>
        {
            using var answer = await StorefrontAsync(replica, hostName);

            return answer.StatusCode == HttpStatusCode.NotFound;
        });

    private static async Task<TimeSpan> TimedAsync(Func<Task<bool>> done)
    {
        var clock = Stopwatch.StartNew();

        while (!await done())
        {
            if (clock.Elapsed > Window * 5)
            {
                return TimeSpan.MaxValue;
            }

            await Task.Delay(20, CancellationToken);
        }

        return clock.Elapsed;
    }

    private static async Task UntilAsync(Func<bool> done, string failure)
    {
        for (var attempt = 0; attempt < 500 && !done(); attempt++)
        {
            await Task.Delay(20, CancellationToken);
        }

        Assert.True(done(), failure);
    }

    private static async Task<HttpResponseMessage> StorefrontAsync(WebApplicationFactory<Program> replica, string hostName)
    {
        using var client = replica.CreateClient();

        return await client.GetAsync($"http://{hostName}/api/storefront/store", CancellationToken);
    }

    // What a dropped network does to the listener's connection, done by the server instead.
    private async Task CutOffAsync(WebApplicationFactory<Program> replica)
    {
        var listener = replica.Services.GetRequiredService<CacheChannel>().ListenerName;

        await QueryAsync(dbContext => dbContext.Database
            .SqlQuery<bool>($"SELECT pg_terminate_backend(pid) AS \"Value\" FROM pg_stat_activity WHERE application_name = {listener}")
            .ToListAsync(CancellationToken));
    }

    private Task<int> SuspendSayingNothingAsync(TestStore store) =>
        QueryAsync(dbContext => dbContext.Database.ExecuteSqlAsync(
            $"UPDATE stores.tenants SET status = 'Suspended' WHERE id = {store.TenantId}",
            CancellationToken));

    private async Task<T> QueryAsync<T>(Func<DbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await query(scope.ServiceProvider.GetRequiredService<DbContext>());
    }

    private async Task<Guid> VerifiedDomainAsync(HttpClient admin, TestStore store, string hostName)
    {
        using var added = await admin.PostAsJsonAsync($"/api/admin/stores/{store.StoreId}/domains", new { HostName = hostName }, CancellationToken);
        var domain = (await added.Content.ReadFromJsonAsync<DomainView>(CancellationToken))!;
        factory.Dns.Publish(domain.ChallengeName!, domain.ChallengeValue!);

        using var verified = await admin.PostAsync($"/api/admin/stores/{store.StoreId}/domains/{domain.Id}/verify", null, CancellationToken);
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (added.StatusCode, verified.StatusCode));

        return domain.Id;
    }

    private sealed record DomainView(Guid Id, string? ChallengeName, string? ChallengeValue);
}
