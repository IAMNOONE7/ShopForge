using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace ShopForge.IntegrationTests.Stores;

public sealed class StoreResolutionTests(ShopForgeApiFactory factory)
{
    [Fact]
    public async Task Store_A_host_resolves_to_store_A()
    {
        var (storeA, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        var store = await GetStoreAsync($"http://{storeA.HostName}");

        Assert.Equal(storeA.StoreId, store.Id);
        Assert.Equal("Store A", store.Name);
    }

    [Fact]
    public async Task Store_B_host_resolves_to_store_B()
    {
        var (_, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        var store = await GetStoreAsync($"http://{storeB.HostName}");

        Assert.Equal(storeB.StoreId, store.Id);
        Assert.Equal("Store B", store.Name);
    }

    [Fact]
    public async Task Unknown_host_returns_404_problem_details()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"http://{TestStores.UniqueHostName("unknown")}/api/storefront/store", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Equal("Store not found", problem?.Title);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(5173)]
    [InlineData(8443)]
    public async Task Port_does_not_affect_resolution(int port)
    {
        var (storeA, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        var store = await GetStoreAsync($"http://{storeA.HostName}:{port}");

        Assert.Equal(storeA.StoreId, store.Id);
    }

    [Fact]
    public async Task Host_matching_ignores_case()
    {
        var (_, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);

        var store = await GetStoreAsync($"http://{storeB.HostName.ToUpperInvariant()}");

        Assert.Equal(storeB.StoreId, store.Id);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_work_without_store_context(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"http://{TestStores.UniqueHostName("unknown")}{path}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<StoreResponse> GetStoreAsync(string origin)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"{origin}/api/storefront/store", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StoreResponse>(TestContext.Current.CancellationToken))!;
    }

    private sealed record StoreResponse(Guid Id, string Name);
}
