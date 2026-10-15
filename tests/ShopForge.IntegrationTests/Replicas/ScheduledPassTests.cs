using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests.Replicas;

// Work every replica can do and one should: the database says when a pass is next due, and the replica that finds it
// due takes it (D-202). Each test schedules a pass of its own name, so the suite's real passes — switched off in every
// test host — are never touched.
public sealed class ScheduledPassTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Eight asks from two replicas at the moment a pass falls due, and one of them gets it.
    [Fact]
    public async Task A_due_pass_is_taken_by_one_replica_however_many_ask_at_once()
    {
        var name = Unique();
        using var other = factory.WithWebHostBuilder(_ => { });
        var schedules = new[] { factory.Services.GetRequiredService<RunSchedule>(), other.Services.GetRequiredService<RunSchedule>() };
        await DueAsync(name);

        var claims = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(ask => Task.Run(() => schedules[ask % 2].ClaimAsync(name, TimeSpan.FromHours(1), CancellationToken), CancellationToken)));
        var dueIn = await DueInAsync(name);

        Assert.Equal(1, claims.Count(claimed => claimed));
        Assert.InRange(dueIn, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
    }

    // A timer started at boot would first fire a period after it; the first time a pass is seen is treated the same
    // way, so a new deployment does not run the nightly work the moment it starts.
    [Fact]
    public async Task A_pass_seen_for_the_first_time_is_due_one_period_later()
    {
        var name = Unique();
        var schedule = factory.Services.GetRequiredService<RunSchedule>();

        var claimed = await schedule.ClaimAsync(name, TimeSpan.FromHours(24), CancellationToken);
        var dueIn = await DueInAsync(name);

        Assert.False(claimed);
        Assert.InRange(dueIn, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24.1));
    }

    // V8 D18: the nightly pass waited a whole day after every start, so a deployment that restarted more often never
    // ran it. Now a replica that starts after the pass fell due runs it at once — and the next one to start does not
    // run it again.
    [Fact]
    public async Task A_replica_starting_after_a_pass_fell_due_runs_it_at_once_and_only_once()
    {
        var name = Unique();
        var pass = new CountingPass(name, TimeSpan.FromHours(24));
        var later = new CountingPass(name, TimeSpan.FromHours(24));
        await DueAsync(name);

        using var started = Scheduling(pass);
        _ = started.Services;
        await UntilAsync(() => pass.Runs == 1);
        using var restarted = Scheduling(later);
        _ = restarted.Services;
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        var dueIn = await DueInAsync(name);

        Assert.Equal((1, 0), (pass.Runs, later.Runs));
        Assert.InRange(dueIn, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24.1));
    }

    // A pass that times out the way an HTTP client does — a cancellation nobody asked for — is a failed pass, logged
    // and run again when it is next due. Before D-202 it ended the worker, and the host stops the application when a
    // background service ends that way.
    [Fact]
    public async Task A_pass_that_times_out_is_run_again_and_the_replica_keeps_running()
    {
        var name = Unique();
        var pass = new CountingPass(name, TimeSpan.FromSeconds(2), timesOut: true);
        await DueAsync(name);

        using var replica = Scheduling(pass);
        _ = replica.Services;
        await UntilAsync(() => pass.Runs >= 2);

        Assert.False(replica.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    // The nightly work used to stop at the first store whose cleanup failed, so every store after it went without.
    [Fact]
    public async Task One_store_s_failing_housekeeping_does_not_stop_the_rest()
    {
        var (failing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var visited = new ConcurrentBag<Guid>();
        using var replica = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStoreMaintenance>();
            services.RemoveAll<IMaintenanceOutsideStores>();
            services.AddScoped<IStoreMaintenance>(provider => new Failing(provider.GetRequiredService<IStoreContext>(), failing.StoreId, visited, timesOut: false));
        }));
        var stores = await StoreCountAsync(replica);

        await replica.Services.GetRequiredService<StoreMaintenance>().RunAsync(CancellationToken);

        Assert.Contains(failing.StoreId, visited);
        Assert.True(visited.Count >= stores, $"{visited.Count} of {stores} stores were looked after");
    }

    // A gateway that does not answer in time for one store is that store's problem. Its cancellation used to slip
    // past the catch that kept one store's failure from the rest, end the pass — and, with it, the worker.
    [Fact]
    public async Task One_store_s_gateway_timing_out_does_not_stop_the_catch_up_for_the_rest()
    {
        var (failing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var visited = new ConcurrentBag<Guid>();
        using var replica = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStoreCatchUp>();
            services.AddScoped<IStoreCatchUp>(provider => new Failing(provider.GetRequiredService<IStoreContext>(), failing.StoreId, visited, timesOut: true));
        }));
        var stores = await StoreCountAsync(replica);

        await replica.Services.GetRequiredService<StoreCatchUp>().RunAsync(CancellationToken);

        Assert.Contains(failing.StoreId, visited);
        Assert.True(visited.Count >= stores, $"{visited.Count} of {stores} stores were caught up");
    }

    private static string Unique() => $"test-pass-{Guid.NewGuid():N}";

    private static async Task<int> StoreCountAsync(WebApplicationFactory<Program> replica)
    {
        await using var scope = replica.Services.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<IStoreDirectory>().AllAsync(CancellationToken)).Count;
    }

    // A replica that keeps its passes, and only the one given: the real ones stay off in the suite.
    private WebApplicationFactory<Program> Scheduling(IScheduledPass pass) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Schedules:Enabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IScheduledPass>();
                services.AddSingleton(pass);
            });
        });

    private Task<int> DueAsync(string name) =>
        QueryAsync(dbContext => dbContext.Database.ExecuteSqlAsync(
            $"INSERT INTO messaging.scheduled_runs (name, due_at) VALUES ({name}, now() - interval '1 hour')",
            CancellationToken));

    private Task<TimeSpan> DueInAsync(string name) =>
        QueryAsync(dbContext => dbContext.Database
            .SqlQuery<TimeSpan>($"SELECT due_at - now() AS \"Value\" FROM messaging.scheduled_runs WHERE name = {name}")
            .SingleAsync(CancellationToken));

    private async Task<T> QueryAsync<T>(Func<DbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await query(scope.ServiceProvider.GetRequiredService<DbContext>());
    }

    private static async Task UntilAsync(Func<bool> done)
    {
        for (var attempt = 0; attempt < 500 && !done(); attempt++)
        {
            await Task.Delay(20, CancellationToken);
        }

        Assert.True(done(), "the pass did not run when it was due");
    }

    // Fails for one store — with an error, or with the cancellation an HTTP client's timeout is — and notes every store
    // it was asked about.
    private sealed class Failing(IStoreContext store, Guid failingStoreId, ConcurrentBag<Guid> visited, bool timesOut) : IStoreMaintenance, IStoreCatchUp
    {
        public string Name => "Failing";

        public Task<int> RunAsync(CancellationToken cancellationToken)
        {
            visited.Add(store.StoreId!.Value);

            if (store.StoreId != failingStoreId)
            {
                return Task.FromResult(0);
            }

            return Task.FromException<int>(timesOut
                ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException())
                : new InvalidOperationException("This store's cleanup cannot run."));
        }
    }

    private sealed class CountingPass(string name, TimeSpan period, bool timesOut = false) : IScheduledPass
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public string Name => name;

        public TimeSpan Period => period;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);

            return timesOut
                ? Task.FromException(new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException()))
                : Task.CompletedTask;
        }
    }
}
