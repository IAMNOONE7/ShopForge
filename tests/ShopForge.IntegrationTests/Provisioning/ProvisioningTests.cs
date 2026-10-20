using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Infrastructure.Auditing;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.Shared.Provisioning;

namespace ShopForge.IntegrationTests.Provisioning;

// The engine against a provider that can be told to fail, hang, answer ambiguously and lose a secret, and a worker
// killed at every step boundary. This is Roadmap V2 V14's acceptance (D-210).
public sealed class ProvisioningTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // A provider that answers at once is followed straight through: one turn of the worker, every step in order.
    [Fact]
    public async Task An_operation_runs_its_steps_in_order_in_one_turn_and_keeps_its_credential_s_secret()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        var id = await host.StartAsync(store);

        var ran = await host.RunAsync(id);
        var operation = await host.ReadAsync(store, id);
        var credential = Assert.Single(host.Provider.Credentials.Values);

        Assert.True(ran);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(TestOperation.Steps, operation.Steps.Select(step => step.Name));
        Assert.All(operation.Steps, step => Assert.Equal(StepStatus.Succeeded, step.Status));
        Assert.Equal(credential.Id, operation.Steps[2].ResourceId);
        Assert.True(operation.Steps[2].KeepsSecret);
        Assert.Equal(credential.Secret, await host.SecretOfAsync(id, 2));
        Assert.NotNull(operation.FinishedAt);
        Assert.Empty(operation.Actions);
    }

    public static TheoryData<string, Behaviour> Boundaries()
    {
        var boundaries = new TheoryData<string, Behaviour>();

        foreach (var point in new[] { "purchase", "resource", "credential.create", "credential.kept", "ready", "approval" })
        {
            boundaries.Add(point, Behaviour.CrashBefore);
            boundaries.Add(point, Behaviour.CrashAfter);
        }

        return boundaries;
    }

    // Killed before a step's work, after the work but before it was written down, or after it was written down and before
    // the next step began: the dead worker's lease holds until it runs out, and then another worker finishes the
    // operation with one of everything, the orphaned credential of a lost secret revoked rather than kept.
    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task A_worker_killed_at_any_step_boundary_is_finished_by_the_next_with_one_of_everything(string point, Behaviour crash)
    {
        var store = await StoreAsync();
        await using var first = ProvisioningHost.Create(factory);
        var id = await first.StartAsync(store);
        await first.CrashAsync(id, point, crash);
        await using var second = first.Replica(factory);

        var takenFromTheDead = await second.RunAsync(id);
        first.Clock.Advance(ProvisioningHost.Lease);
        var operation = await second.RunToRestAsync(store, id);
        var provider = first.Provider;
        var live = Assert.Single(provider.Credentials.Values, credential => !credential.Revoked);

        Assert.False(takenFromTheDead);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Single(provider.Purchases);
        Assert.Single(provider.Resources);
        Assert.Equal(1, provider.EffectsOf("purchase"));
        Assert.Equal(1, provider.EffectsOf("resource"));
        Assert.Equal(point == "credential.create" && crash == Behaviour.CrashAfter ? 2 : 1, provider.EffectsOf("credential.create"));
        Assert.Equal(provider.EffectsOf("credential.create") - 1, provider.Credentials.Values.Count(credential => credential.Revoked));
        Assert.Equal(live.Id, operation.Steps[2].ResourceId);
        Assert.Equal(live.Secret, await second.SecretOfAsync(id, 2));
        Assert.All(operation.Steps.Take(3), step => Assert.True(step.Created));
    }

    [Fact]
    public async Task Two_workers_reaching_for_one_operation_at_once_do_its_work_once()
    {
        var store = await StoreAsync();
        await using var first = ProvisioningHost.Create(factory);
        await using var second = first.Replica(factory);
        var id = await first.StartAsync(store);

        var claimed = await Task.WhenAll(first.RunAsync(id), second.RunAsync(id));
        var operation = await first.RunToRestAsync(store, id);

        Assert.Single(claimed, ran => ran);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(1, first.Provider.EffectsOf("purchase"));
        Assert.Equal(1, first.Provider.EffectsOf("resource"));
        Assert.Equal(1, first.Provider.EffectsOf("credential.create"));
    }

    // Each step it finishes renews the lease, so a long operation is not taken from a worker still going through it. Half
    // a lease passes at each step: by the second, a lease that was never renewed has run out, and one that was renewed
    // still has half its time, however slowly this machine is going.
    [Fact]
    public async Task A_worker_going_through_many_steps_keeps_its_lease_at_each_one()
    {
        var store = await StoreAsync();
        await using var first = ProvisioningHost.Create(factory);
        await using var second = first.Replica(factory);
        var id = await first.StartAsync(store);
        var taken = new List<bool>();
        first.Provider.During = async () =>
        {
            first.Clock.Advance(ProvisioningHost.Lease / 2);
            taken.Add(await second.RunAsync(id));
        };
        first.Provider.Next("purchase", Behaviour.Interleave);
        first.Provider.Next("resource", Behaviour.Interleave);
        first.Provider.Next("credential.create", Behaviour.Interleave);

        var ran = await first.RunAsync(id);
        var operation = await first.ReadAsync(store, id);

        Assert.True(ran);
        Assert.Equal([false, false, false], taken);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(1, first.Provider.EffectsOf("resource"));
    }

    [Fact]
    public async Task A_create_whose_answer_was_lost_is_found_on_the_next_try_not_made_again()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Next("resource", Behaviour.AnswerLost);
        var id = await host.StartAsync(store);

        await host.RunAsync(id);
        var afterTheLoss = await host.ReadAsync(store, id);
        var operation = await host.RunToRestAsync(store, id);

        Assert.Equal((StepStatus.Pending, 1), (afterTheLoss.Steps[1].Status, afterTheLoss.Steps[1].Attempts));
        Assert.Contains("lost", afterTheLoss.Steps[1].LastError, StringComparison.Ordinal);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(1, host.Provider.EffectsOf("resource"));
        Assert.True(operation.Steps[1].Created);
    }

    [Fact]
    public async Task A_provider_that_never_answers_is_given_up_on_after_the_step_s_timeout_and_asked_again()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Next("ready", Behaviour.Hang);
        var id = await host.StartAsync(store);

        await host.RunAsync(id);
        var afterTheHang = await host.ReadAsync(store, id);
        var operation = await host.RunToRestAsync(store, id);

        Assert.Equal((StepStatus.Pending, 1), (afterTheHang.Steps[3].Status, afterTheHang.Steps[3].Attempts));
        Assert.Contains($"No answer within {ProvisioningHost.StepTimeout.TotalSeconds:0} seconds", afterTheHang.Steps[3].LastError, StringComparison.Ordinal);
        Assert.Equal(OperationState.Succeeded, operation.State);
    }

    [Fact]
    public async Task A_credential_whose_secret_was_lost_is_revoked_and_replaced_never_recorded_as_configured()
    {
        var store = await StoreAsync();
        await using var first = ProvisioningHost.Create(factory);
        var id = await first.StartAsync(store);
        await first.CrashAsync(id, "credential.create", Behaviour.CrashAfter);
        var orphan = Assert.Single(first.Provider.Credentials.Values);
        var afterTheCrash = await first.ReadAsync(store, id);
        await using var second = first.Replica(factory);

        first.Clock.Advance(ProvisioningHost.Lease);
        var operation = await second.RunToRestAsync(store, id);
        var replacement = Assert.Single(first.Provider.Credentials.Values, credential => !credential.Revoked);

        Assert.False(afterTheCrash.Steps[2].KeepsSecret);
        Assert.Null(afterTheCrash.Steps[2].ResourceId);
        Assert.True(orphan.Revoked);
        Assert.NotEqual(orphan.Id, replacement.Id);
        Assert.Equal(replacement.Id, operation.Steps[2].ResourceId);
        Assert.Equal(replacement.Secret, await second.SecretOfAsync(id, 2));
    }

    // The vault first, then the row: a vault that fails as the secret is kept leaves no row claiming a secret that is
    // not there, and the next try replaces the credential whose secret it never kept.
    [Fact]
    public async Task A_vault_that_fails_while_a_secret_is_kept_never_leaves_a_credential_recorded_without_it()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Secrets.FailNextWrite();
        var id = await host.StartAsync(store);

        await host.RunAsync(id);
        var afterTheFailure = await host.ReadAsync(store, id);
        var operation = await host.RunToRestAsync(store, id);
        var live = Assert.Single(host.Provider.Credentials.Values, credential => !credential.Revoked);

        Assert.False(afterTheFailure.Steps[2].KeepsSecret);
        Assert.Contains("vault", afterTheFailure.Steps[2].LastError, StringComparison.Ordinal);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(2, host.Provider.EffectsOf("credential.create"));
        Assert.Equal(live.Secret, await host.SecretOfAsync(id, 2));
    }

    [Fact]
    public async Task Waiting_is_looked_at_again_on_a_growing_backoff_and_uses_up_no_tries()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Ready = false;
        var id = await host.StartAsync(store);

        await host.RunAsync(id);
        var firstLook = await host.ReadAsync(store, id);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunAsync(id);
        var secondLook = await host.ReadAsync(store, id);
        host.Provider.Ready = true;
        var operation = await host.RunToRestAsync(store, id);
        var audit = await host.AuditAsync(store, id);

        Assert.Equal((StepStatus.Waiting, 1, 0), (firstLook.Steps[3].Status, firstLook.Steps[3].Checks, firstLook.Steps[3].Attempts));
        Assert.Equal((2, 0), (secondLook.Steps[3].Checks, secondLook.Steps[3].Attempts));
        Assert.Equal(TimeSpan.FromMinutes(1), firstLook.NextCheckAt - firstLook.ChangedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), secondLook.NextCheckAt - secondLook.ChangedAt);
        Assert.Equal("The certificate is being issued.", secondLook.Steps[3].Note);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(0, operation.Steps[3].Attempts);
        Assert.Single(audit, entry => entry.Action == "operation.step-waiting");
    }

    [Fact]
    public async Task A_step_that_keeps_failing_stops_for_a_person_and_a_retry_resumes_from_it()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Next("resource", [.. Enumerable.Repeat(Behaviour.Fail, 8)]);
        var id = await host.StartAsync(store);

        var stopped = await host.RunToRestAsync(store, id);
        var retried = await host.ActAsync(store, admin => admin.RetryAsync(id, CancellationToken));
        var operation = await host.RunToRestAsync(store, id);

        Assert.Equal(OperationState.Failed, stopped.State);
        Assert.Equal((StepStatus.Failed, 8), (stopped.Steps[1].Status, stopped.Steps[1].Attempts));
        Assert.Contains("unavailable", stopped.Steps[1].LastError, StringComparison.Ordinal);
        Assert.Equal(["retry", "cancel"], stopped.Actions);
        Assert.Equal(OperationState.Running, retried.Operation?.State);
        Assert.Equal((StepStatus.Pending, 0), (retried.Operation?.Steps[1].Status, retried.Operation?.Steps[1].Attempts));
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(1, host.Provider.EffectsOf("purchase"));
    }

    [Fact]
    public async Task A_refusal_stops_the_operation_at_once()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Next("resource", Behaviour.Refuse);
        var id = await host.StartAsync(store);

        var operation = await host.RunToRestAsync(store, id);

        Assert.Equal(OperationState.Failed, operation.State);
        Assert.Equal((StepStatus.Failed, 1), (operation.Steps[1].Status, operation.Steps[1].Attempts));
        Assert.Contains("refused", operation.Steps[1].LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_that_needs_a_person_waits_for_them_and_goes_on_when_told()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Approved = false;
        var id = await host.StartAsync(store);

        var waiting = await host.RunToRestAsync(store, id);
        var lookedAtAnyway = await host.RunAsync(id);
        host.Provider.Approved = true;
        var continued = await host.ActAsync(store, admin => admin.ContinueAsync(id, CancellationToken));
        var operation = await host.RunToRestAsync(store, id);

        Assert.Equal((OperationState.AwaitingInput, StepStatus.AwaitingInput), (waiting.State, waiting.Steps[4].Status));
        Assert.Contains("nameservers", waiting.Steps[4].Note, StringComparison.Ordinal);
        Assert.Null(waiting.NextCheckAt);
        Assert.Equal(["continue", "cancel"], waiting.Actions);
        Assert.False(lookedAtAnyway);
        Assert.Equal(OperationState.Running, continued.Operation?.State);
        Assert.Equal((StepStatus.Pending, null), (continued.Operation?.Steps[4].Status, continued.Operation?.Steps[4].Note));
        Assert.Equal(OperationState.Succeeded, operation.State);
    }

    // Only what the operation made is taken away: what was bought is kept, and what it found was never its own.
    [Fact]
    public async Task A_cancelled_operation_takes_away_only_what_it_made_and_keeps_what_was_bought()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Resources[TestOperation.StableName(store.StoreId, "resource")] = new Made("resource-made-before", Guid.CreateVersion7());
        host.Provider.Approved = false;
        var id = await host.StartAsync(store);
        await host.RunToRestAsync(store, id);

        var cancelling = await host.ActAsync(store, admin => admin.CancelAsync(id, CancellationToken));
        var operation = await host.RunToRestAsync(store, id);
        var credential = Assert.Single(host.Provider.Credentials.Values);

        Assert.Equal(OperationState.Cancelling, cancelling.Operation?.State);
        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.NotNull(operation.FinishedAt);
        Assert.Equal(StepStatus.Kept, operation.Steps[0].Status);
        Assert.Single(host.Provider.Purchases);
        Assert.Equal((StepStatus.Succeeded, false), (operation.Steps[1].Status, operation.Steps[1].Created));
        Assert.Equal("resource-made-before", Assert.Single(host.Provider.Resources).Value.Id);
        Assert.Equal(StepStatus.Undone, operation.Steps[2].Status);
        Assert.True(credential.Revoked);
        Assert.Null(await host.SecretOfAsync(id, 2));
        Assert.Equal(StepStatus.Pending, operation.Steps[4].Status);
    }

    // A clean-up that keeps failing is a person's to look at, and retrying it finishes the cancellation.
    [Fact]
    public async Task A_clean_up_that_keeps_failing_stops_for_a_person_and_a_retry_finishes_it()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        host.Provider.Approved = false;
        var id = await host.StartAsync(store);
        await host.RunToRestAsync(store, id);
        host.Provider.Next("resource.delete", [.. Enumerable.Repeat(Behaviour.Fail, 8)]);

        await host.ActAsync(store, admin => admin.CancelAsync(id, CancellationToken));
        var stopped = await host.RunToRestAsync(store, id);
        var retried = await host.ActAsync(store, admin => admin.RetryAsync(id, CancellationToken));
        var operation = await host.RunToRestAsync(store, id);

        Assert.Equal(OperationState.Failed, stopped.State);
        Assert.Equal((StepStatus.Undone, StepStatus.Succeeded, 8), (stopped.Steps[2].Status, stopped.Steps[1].Status, stopped.Steps[1].Attempts));
        Assert.Equal(["retry"], stopped.Actions);
        Assert.Equal(OperationState.Cancelling, retried.Operation?.State);
        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.Equal(StepStatus.Undone, operation.Steps[1].Status);
        Assert.Empty(host.Provider.Resources);
    }

    [Fact]
    public async Task A_cancelled_operation_that_made_nothing_is_simply_over()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        var id = await host.StartAsync(store);

        var cancelling = await host.ActAsync(store, admin => admin.CancelAsync(id, CancellationToken));
        var operation = await host.RunToRestAsync(store, id);

        Assert.Equal(OperationState.Cancelling, cancelling.Operation?.State);
        Assert.Equal(OperationState.Cancelled, operation.State);
        Assert.All(operation.Steps, step => Assert.Equal(StepStatus.Pending, step.Status));
        Assert.Empty(host.Provider.Effects);
    }

    // The friendly answer reads first; the unique index is what holds when the reading is too slow.
    [Fact]
    public async Task A_store_runs_one_operation_at_a_time_and_a_subject_is_worked_on_by_one()
    {
        var (store, sibling) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var host = ProvisioningHost.Create(factory);
        var subject = TestStores.UniqueHostName();
        var id = await host.StartAsync(store, subject);

        var again = await host.TryStartAsync(store);
        var elsewhere = await host.TryStartAsync(sibling, subject);
        await host.RunToRestAsync(store, id);
        var afterwards = await host.TryStartAsync(sibling, subject);

        Assert.Equal(new OperationStart(id, Started: false), again);
        Assert.Equal(new OperationStart(id, Started: false), elsewhere);
        Assert.True(afterwards.Started);
    }

    [Fact]
    public async Task Starts_arriving_together_leave_one_unfinished_operation()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);

        var starts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                return await host.TryStartAsync(store);
            }
            catch (DbUpdateException)
            {
                return null;
            }
        }));
        await using var scope = TestStores.CreateScope(host.Host.Services, store);
        var unfinished = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<ProvisioningOperation>()
            .CountAsync(operation => operation.FinishedAt == null, CancellationToken);

        Assert.Single(starts, start => start is { Started: true });
        Assert.Equal(1, unfinished);
    }

    [Fact]
    public async Task Every_step_s_change_is_in_the_audit_trail_with_the_provider_s_ids_and_no_secret()
    {
        var store = await StoreAsync();
        await using var host = ProvisioningHost.Create(factory);
        var id = await host.StartAsync(store);
        await host.RunToRestAsync(store, id);

        var audit = await host.AuditAsync(store, id);
        var secret = await host.SecretOfAsync(id, 2);
        var succeeded = audit.Where(entry => entry.Action == "operation.step-succeeded").ToList();

        Assert.Single(audit, entry => entry.Action == "operation.started");
        Assert.Single(audit, entry => entry.Action == "operation.succeeded");
        Assert.Equal(5, succeeded.Count);
        Assert.Equal(3, succeeded.Count(entry => entry.Details!.Contains("\"requestId\":\"request-", StringComparison.Ordinal)));
        Assert.NotNull(secret);
        Assert.DoesNotContain(audit, entry => entry.Details!.Contains(secret, StringComparison.Ordinal));
        Assert.All(audit, entry => Assert.Equal(AuditActorKind.System, entry.ActorKind));
    }

    [Fact]
    public async Task An_operation_this_build_does_not_have_is_not_started()
    {
        var store = await StoreAsync();
        await using var scope = TestStores.CreateScope(factory.Services, store);
        var provisioning = scope.ServiceProvider.GetRequiredService<IProvisioning>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioning.StartAsync("test.unknown", null, null, CancellationToken));
    }

    // A lease that ran out while its step was still running would let a second replica start the same step.
    [Fact]
    public void A_lease_no_longer_than_a_step_s_timeout_stops_the_application_starting()
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Provisioning:LeaseSeconds", "60")
            .UseSetting("Provisioning:StepTimeoutSeconds", "60"));

        var refused = Assert.ThrowsAny<Exception>(() => misconfigured.Services);

        Assert.Contains("LeaseSeconds", refused.ToString(), StringComparison.Ordinal);
    }

    private async Task<TestStore> StoreAsync() => (await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services)).A;
}

// Alone, because the worker it starts carries every operation that is due, and another test's would be among them.
[CollectionDefinition(nameof(ProvisioningWorkerCollection), DisableParallelization = true)]
public sealed class ProvisioningWorkerCollection;

[Collection(nameof(ProvisioningWorkerCollection))]
public sealed class ProvisioningWorkerTests(ShopForgeApiFactory factory)
{
    // Started and left alone: the worker every replica runs carries it through without anybody calling it.
    [Fact]
    public async Task The_worker_carries_an_operation_through_by_itself()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        await using var host = ProvisioningHost.Create(factory, workerRunning: true);
        var id = await host.StartAsync(store);

        var operation = await host.ReadAsync(store, id);

        for (var look = 0; look < 150 && operation.State != OperationState.Succeeded; look++)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
            operation = await host.ReadAsync(store, id);
        }

        Assert.Equal(OperationState.Succeeded, operation.State);
    }
}
