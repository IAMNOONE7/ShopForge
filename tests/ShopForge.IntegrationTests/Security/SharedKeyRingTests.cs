using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ShopForge.IntegrationTests.Security;

// Every cookie, antiforgery token and protected link is sealed with a key ring. Left to its default, each replica
// keeps its own in its own filesystem: a cookie one replica issued is unreadable by the next, so a sign-in does
// not survive the load balancer, and a container restart — which every deployment is — signs everybody out. On
// one developer machine it never shows, because the ring sits in the home directory and outlives every restart
// (D-191).
public sealed class SharedKeyRingTests(ShopForgeApiFactory factory) : IDisposable
{
    // A second host over the same database: a second replica, as far as the key ring is concerned.
    private readonly WebApplicationFactory<Program> _otherReplica = factory.WithWebHostBuilder(_ => { });

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _otherReplica.Dispose();

    [Fact]
    public async Task A_sign_in_on_one_replica_is_honoured_by_another()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId);

        using var first = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var signedIn = await first.PostAsJsonAsync("/api/admin/auth/login", new { user.Email, user.Password }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        var cookie = string.Join("; ", signedIn.Headers.GetValues("Set-Cookie").Select(header => header.Split(';')[0]));

        using var second = _otherReplica.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/stores");
        request.Headers.Add("Cookie", cookie);
        using var answered = await second.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
    }

    // The test above would pass on a test machine even without the fix, because every host here shares one home
    // directory. This is what pins it: the ring lives in the database every replica shares, and nowhere a
    // container restart can lose it.
    [Fact]
    public async Task The_key_ring_lives_in_the_database()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var signedIn = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

        var keys = await factory.QueryAsync(store, dbContext => dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM security.data_protection_keys")
            .SingleAsync(CancellationToken));

        Assert.True(keys > 0, "No key in security.data_protection_keys: the ring is being kept somewhere a restart loses it.");
    }
}
