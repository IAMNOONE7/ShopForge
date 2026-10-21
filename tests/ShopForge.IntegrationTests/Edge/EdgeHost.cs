using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.IntegrationTests.Provisioning;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Stores;
using ShopForge.Stores.Edge;

namespace ShopForge.IntegrationTests.Edge;

// The application binding domains through a recorded Front Door and running zones at a recorded Cloudflare, with a
// clock of the test's own (D-212). The world's DNS answers from the zones the platform runs once they are delegated,
// and from what a merchant publishes at their own DNS host otherwise.
internal sealed class EdgeHost : IAsyncDisposable
{
    // Long enough that a busy machine's few local calls inside a step are not taken for a provider that stopped
    // answering, short enough that the test of one that does stop is not slow; the lease outlasts it.
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);

    private EdgeHost(WebApplicationFactory<Program> host, RecordedFrontDoor frontDoor, RecordedCloudflare cloudflare, TestClock clock)
    {
        Host = host;
        FrontDoor = frontDoor;
        Cloudflare = cloudflare;
        Clock = clock;
    }

    public WebApplicationFactory<Program> Host { get; }

    public RecordedFrontDoor FrontDoor { get; }

    public RecordedCloudflare Cloudflare { get; }

    public TestClock Clock { get; }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static async Task<EdgeHost> StartAsync(ShopForgeApiFactory factory, bool withZones = true)
    {
        var clock = new TestClock();
        var cloudflare = await RecordedCloudflare.StartAsync(clock);
        var world = new WorldDns(cloudflare, factory.Dns);
        var frontDoor = await RecordedFrontDoor.StartAsync(clock, world.Lookup);
        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Edge:Domains:SubscriptionId", RecordedFrontDoor.Subscription);
            builder.UseSetting("Edge:Domains:ResourceGroup", RecordedFrontDoor.Group);
            builder.UseSetting("Edge:Domains:Profile", RecordedFrontDoor.Profile);
            builder.UseSetting("Edge:Domains:Endpoint", RecordedFrontDoor.Endpoint);
            builder.UseSetting("Edge:Domains:EndpointHost", RecordedFrontDoor.EndpointHost);
            builder.UseSetting("Edge:Domains:WafPolicyId", RecordedFrontDoor.WafPolicyId);
            builder.UseSetting("Edge:Domains:ManagementUrl", frontDoor.BaseUrl);

            if (withZones)
            {
                builder.UseSetting("Dns:Cloudflare:ApiToken", RecordedCloudflare.Token);
                builder.UseSetting("Dns:Cloudflare:AccountId", RecordedCloudflare.Account);
                builder.UseSetting("Dns:Cloudflare:BaseUrl", cloudflare.BaseUrl);
            }

            builder.UseSetting("Provisioning:StepTimeoutSeconds", $"{StepTimeout.TotalSeconds}");
            builder.UseSetting("Provisioning:LeaseSeconds", $"{StepTimeout.TotalSeconds * 3}");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<TokenCredential>(new MadeUpArmCredential(clock));
                services.AddSingleton<IDnsTxtRecords>(world);
            });
        });

        return new EdgeHost(host, frontDoor, cloudflare, clock);
    }

    // A store and its owner, signed in.
    public async Task<OwnedStore> StoreAsync()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(Host.Services);
        var owner = await TestUsers.LoginAsync(Host, await TestUsers.CreateAsync(Host.Services, store.TenantId));

        return new OwnedStore(store, owner);
    }

    public static async Task<JsonElement> ClaimAsync(OwnedStore owned, string hostName)
    {
        using var added = await owned.Owner.PostAsJsonAsync($"/api/admin/stores/{owned.Store.StoreId}/domains", new { HostName = hostName }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        return JsonDocument.Parse(await added.Content.ReadAsStringAsync(CancellationToken)).RootElement.Clone();
    }

    // A merchant proving a domain at their own DNS host, as D-124 has them do: the binding starts with the proof.
    public async Task<Guid> ProveAsync(ShopForgeApiFactory factory, OwnedStore owned, string hostName)
    {
        var claim = await ClaimAsync(owned, hostName);
        factory.Dns.Publish(claim.GetProperty("challengeName").GetString()!, claim.GetProperty("challengeValue").GetString()!);

        using var verified = await owned.Owner.PostAsync(
            $"/api/admin/stores/{owned.Store.StoreId}/domains/{claim.GetProperty("id").GetGuid()}/verify", content: null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return (await DomainAsync(owned, hostName)).GetProperty("edge").GetProperty("operationId").GetGuid();
    }

    public static async Task<JsonElement> DomainAsync(OwnedStore owned, string hostName)
    {
        var domains = JsonDocument.Parse(await owned.Owner.GetStringAsync($"/api/admin/stores/{owned.Store.StoreId}/domains", CancellationToken)).RootElement;

        return domains.EnumerateArray().Single(domain => domain.GetProperty("hostName").GetString() == hostName).Clone();
    }

    public Task<bool> RunAsync(Guid operationId) =>
        Host.Services.GetRequiredService<OperationRunner>().RunAsync(operationId, CancellationToken);

    // Runs the operation as a worker that is never stopped would, moving the clock on by the given step after each
    // round, until it rests: finished, failed, or waiting for a person.
    public async Task<OperationDetail> RunToRestAsync(TestStore store, Guid operationId, TimeSpan? step = null)
    {
        for (var round = 0; round < 60; round++)
        {
            await RunAsync(operationId);
            var operation = await OperationAsync(store, operationId);

            if (operation.State is not (OperationState.Running or OperationState.Cancelling))
            {
                return operation;
            }

            Clock.Advance(step ?? TimeSpan.FromHours(1));
        }

        throw new InvalidOperationException("The operation did not come to rest.");
    }

    public async Task<OperationDetail> OperationAsync(TestStore store, Guid operationId)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        return (await scope.ServiceProvider.GetRequiredService<OperationAdmin>().FindAsync(operationId, CancellationToken))!;
    }

    public async Task<OperationActionResult> CancelAsync(TestStore store, Guid operationId)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        return await scope.ServiceProvider.GetRequiredService<OperationAdmin>().CancelAsync(operationId, CancellationToken);
    }

    // The pass for one store: the suite's stores share one database, and another test's domains are not this test's.
    public Task<int> BindingsPassAsync(TestStore store) =>
        Host.Services.GetRequiredService<DomainBindingsPass>().RunForStoreAsync(new StoreReference(store.StoreId, store.TenantId), CancellationToken);

    public Task<int> ValidationsPassAsync() => Host.Services.GetRequiredService<EdgeValidationsPass>().RunAsync(CancellationToken);

    // The operations of a store on a host, newest first.
    public async Task<List<OperationSummary>> OperationsOnAsync(TestStore store, string subject)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        return [.. (await scope.ServiceProvider.GetRequiredService<OperationAdmin>().ListAsync(CancellationToken)).Where(operation => operation.Subject == subject)];
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await FrontDoor.DisposeAsync();
        await Cloudflare.DisposeAsync();
    }
}

internal sealed record OwnedStore(TestStore Store, HttpClient Owner);

// The world's resolvers: a delegated zone of the platform's answers for the names under it; anything else answers what
// its owner published at their own DNS host.
internal sealed class WorldDns(RecordedCloudflare cloudflare, FakeDnsTxtRecords merchants) : IDnsTxtRecords
{
    public IReadOnlyList<string> Lookup(string name) =>
        cloudflare.PublicTxt(name) is { Count: > 0 } served ? served : merchants.LookupAsync(name, CancellationToken.None).Result;

    public Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Lookup(name));
}

// What the app's managed identity hands it in a deployment: a token for Resource Manager, here one the recorded Front
// Door accepts.
internal sealed class MadeUpArmCredential(TimeProvider clock) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(RecordedFrontDoor.Token, clock.GetUtcNow() + TimeSpan.FromHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
