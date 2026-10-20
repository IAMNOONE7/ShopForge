using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShopForge.Access.Domain;

namespace ShopForge.IntegrationTests.Provisioning;

// A store's operations as its admin reads them, and the three things a manager can do about one that stopped (D-210).
public sealed class OperationAdminTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_store_reads_its_operations_and_every_step_but_never_a_secret()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        using var admin = await SignInAsync(host, store, TenantRole.Owner);
        var id = await host.StartAsync(store);
        await host.RunToRestAsync(store, id);
        var secret = await host.SecretOfAsync(id, 2);

        using var listed = await admin.GetAsync($"/api/admin/stores/{store.StoreId}/operations", CancellationToken);
        using var read = await admin.GetAsync($"/api/admin/stores/{store.StoreId}/operations/{id}", CancellationToken);
        var operations = await listed.Content.ReadFromJsonAsync<List<JsonElement>>(CancellationToken);
        var body = await read.Content.ReadAsStringAsync(CancellationToken);
        var operation = JsonDocument.Parse(body).RootElement;

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (listed.StatusCode, read.StatusCode));
        Assert.Equal(id, Assert.Single(operations!).GetProperty("id").GetGuid());
        Assert.Equal("succeeded", operation.GetProperty("state").GetString());
        Assert.Equal(TestOperation.Steps, operation.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("name").GetString()));
        Assert.True(operation.GetProperty("steps")[2].GetProperty("keepsSecret").GetBoolean());
        Assert.NotNull(secret);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_manager_moves_a_stopped_operation_on_and_is_told_when_an_action_does_not_apply()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        using var admin = await SignInAsync(host, store, TenantRole.Owner);
        host.Provider.Approved = false;
        var id = await host.StartAsync(store);
        await host.RunToRestAsync(store, id);
        var operations = $"/api/admin/stores/{store.StoreId}/operations/{id}";

        using var retried = await admin.PostAsync($"{operations}/retry", content: null, CancellationToken);
        using var continued = await admin.PostAsync($"{operations}/continue", content: null, CancellationToken);
        var stillWaiting = await host.RunToRestAsync(store, id);
        using var cancelled = await admin.PostAsync($"{operations}/cancel", content: null, CancellationToken);
        var over = await host.RunToRestAsync(store, id);
        using var cancelledAgain = await admin.PostAsync($"{operations}/cancel", content: null, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, retried.StatusCode);
        Assert.Equal(HttpStatusCode.OK, continued.StatusCode);
        Assert.Equal("running", JsonDocument.Parse(await continued.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("state").GetString());
        Assert.Equal(Infrastructure.Provisioning.OperationState.AwaitingInput, stillWaiting.State);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(Infrastructure.Provisioning.OperationState.Cancelled, over.State);
        Assert.Equal(HttpStatusCode.Conflict, cancelledAgain.StatusCode);
    }

    // A step that is running is not interrupted; the manager is told to wait until it has stopped.
    [Fact]
    public async Task Nobody_acts_on_an_operation_while_one_of_its_steps_is_running()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        using var admin = await SignInAsync(host, store, TenantRole.Owner);
        var id = await host.StartAsync(store);
        await host.CrashAsync(id, "purchase", Behaviour.CrashBefore);

        using var cancelled = await admin.PostAsync($"/api/admin/stores/{store.StoreId}/operations/{id}/cancel", content: null, CancellationToken);
        var refusal = await cancelled.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
        Assert.Contains("running", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Staff_who_do_not_manage_the_store_read_its_operations_but_do_not_act_on_them()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        using var support = await SignInAsync(host, store, TenantRole.Support);
        host.Provider.Approved = false;
        var id = await host.StartAsync(store);
        await host.RunToRestAsync(store, id);

        using var read = await support.GetAsync($"/api/admin/stores/{store.StoreId}/operations/{id}", CancellationToken);
        using var cancelled = await support.PostAsync($"/api/admin/stores/{store.StoreId}/operations/{id}/cancel", content: null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancelled.StatusCode);
    }

    private async Task<TestStore> StoreAsync() => (await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services)).A;

    private static async Task<HttpClient> SignInAsync(ProvisioningHost host, TestStore store, TenantRole role) =>
        await TestUsers.LoginAsync(host.Host, await TestUsers.CreateAsync(host.Host.Services, store.TenantId, role));
}
