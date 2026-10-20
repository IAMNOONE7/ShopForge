using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Auditing;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.IntegrationTests.Payments;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Provisioning;

// A replica of the application for one test: its own provider, secrets and clock, on the suite's database. Two built
// over the same provider, secrets and clock are two replicas of one deployment, which is how a worker is killed and
// another one takes over (D-210).
internal sealed class ProvisioningHost : IAsyncDisposable
{
    // Short enough that a test outlasts them by moving the clock, long enough that a busy machine's database write inside
    // a step is not mistaken for a provider that stopped answering; the lease outlasts the step's timeout.
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);

    private ProvisioningHost(WebApplicationFactory<Program> host, FakeProvider provider, InMemorySecrets secrets, TestClock clock)
    {
        Host = host;
        Provider = provider;
        Secrets = secrets;
        Clock = clock;
    }

    public WebApplicationFactory<Program> Host { get; }

    public FakeProvider Provider { get; }

    public InMemorySecrets Secrets { get; }

    public TestClock Clock { get; }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static ProvisioningHost Create(ShopForgeApiFactory factory, bool workerRunning = false) =>
        Create(factory, new FakeProvider(), new InMemorySecrets(), new TestClock(), workerRunning);

    // Another replica of the same deployment: the same provider, the same vault, the same time.
    public ProvisioningHost Replica(ShopForgeApiFactory factory) => Create(factory, Provider, Secrets, Clock, workerRunning: false);

    public async Task<Guid> StartAsync(TestStore store, string? subject = null)
    {
        var started = await TryStartAsync(store, subject);
        Assert.True(started.Started);

        return started.OperationId;
    }

    public async Task<OperationStart> TryStartAsync(TestStore store, string? subject = null)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        return await scope.ServiceProvider.GetRequiredService<IProvisioning>()
            .StartAsync(TestOperation.Kind, subject, new { Plan = "basic" }, CancellationToken);
    }

    public Task<bool> RunAsync(Guid operationId, CancellationToken? cancellationToken = null) =>
        Host.Services.GetRequiredService<OperationRunner>().RunAsync(operationId, cancellationToken ?? CancellationToken);

    // Runs the operation as a worker that is never stopped would, moving the clock past every wait and every backoff,
    // until it rests: finished, failed, or waiting for a person.
    public async Task<OperationDetail> RunToRestAsync(TestStore store, Guid operationId)
    {
        for (var round = 0; round < 50; round++)
        {
            await RunAsync(operationId);
            var operation = await ReadAsync(store, operationId);

            if (operation.State is not (OperationState.Running or OperationState.Cancelling))
            {
                return operation;
            }

            Clock.Advance(TimeSpan.FromHours(1));
        }

        throw new InvalidOperationException("The operation did not come to rest.");
    }

    // A worker that dies at the point the provider is told to stop it, holding the operation's lease.
    public async Task CrashAsync(Guid operationId, string point, Behaviour crash)
    {
        using var worker = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        Provider.Worker = worker;
        Provider.Next(point, crash);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(operationId, worker.Token));
        Provider.Worker = null;
    }

    public async Task<OperationDetail> ReadAsync(TestStore store, Guid operationId)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        return await scope.ServiceProvider.GetRequiredService<OperationAdmin>().FindAsync(operationId, CancellationToken)
            ?? throw new InvalidOperationException("The operation is not there.");
    }

    public async Task<OperationActionResult> ActAsync(TestStore store, Func<OperationAdmin, Task<OperationActionResult>> act)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);

        return await act(scope.ServiceProvider.GetRequiredService<OperationAdmin>());
    }

    public async Task<string?> SecretOfAsync(Guid operationId, int position) =>
        await Secrets.FindAsync($"provisioning-{operationId:N}-{position}", CancellationToken);

    public async Task<List<AuditEntry>> AuditAsync(TestStore store, Guid operationId)
    {
        await using var scope = TestStores.CreateScope(Host.Services, store);
        var marker = operationId.ToString();

        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<AuditEntry>()
            .Where(entry => entry.Action.StartsWith("operation.") && entry.Details != null && entry.Details.Contains(marker))
            .OrderBy(entry => entry.RecordedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(CancellationToken);
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    private static ProvisioningHost Create(ShopForgeApiFactory factory, FakeProvider provider, InMemorySecrets secrets, TestClock clock, bool workerRunning)
    {
        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Provisioning:Enabled", workerRunning ? "true" : "false");
            builder.UseSetting("Provisioning:IntervalSeconds", "1");
            builder.UseSetting("Provisioning:StepTimeoutSeconds", $"{StepTimeout.TotalSeconds}");
            builder.UseSetting("Provisioning:LeaseSeconds", $"{Lease.TotalSeconds}");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(provider);
                services.AddSingleton<ISecretStore>(secrets);
                services.AddSingleton<TimeProvider>(clock);
            });
        });

        return new ProvisioningHost(host, provider, secrets, clock);
    }
}

// The machine's time and however far the test has moved it on. Timers keep the machine's own time.
internal sealed class TestClock : TimeProvider
{
    private long _ticks;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
