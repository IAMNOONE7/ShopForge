using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Provisioning;

// Carries operations through (D-210). It claims one that is due, runs its current step under a timeout, writes down how
// that went, goes straight on while the next step is ready, and lets go. The claim is one conditional update, as the
// outbox's is, so of two replicas asking at once only one gets it. A worker stopped mid-step writes nothing, exactly as a
// crash would, and the lease running out is what lets another take the step up again.
internal sealed class OperationRunner(IServiceProvider services, ProvisioningOptions options, TimeProvider clock, ILogger<OperationRunner> logger)
{
    private const int Batch = 20;

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var ran = 0;

        foreach (var operationId in await DueAsync(cancellationToken))
        {
            ran += await RunAsync(operationId, cancellationToken) ? 1 : 0;
        }

        return ran;
    }

    // False when the operation is not due, or another worker holds it.
    public async Task<bool> RunAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!await ClaimAsync(operationId, cancellationToken))
        {
            return false;
        }

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var operation = await dbContext.Set<ProvisioningOperation>()
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Steps)
            .SingleAsync(candidate => candidate.Id == operationId, cancellationToken);

        // Its steps work inside the store it belongs to, and everything they write is held to that store.
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(operation.StoreId, operation.TenantId);

        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        var steps = scope.ServiceProvider.GetServices<IOperationStep>().Where(step => step.Kind == operation.Kind).ToList();

        try
        {
            for (var turn = 0; turn < ProvisioningOptions.Turns && IsDue(operation); turn++)
            {
                var (step, happened) = operation.State == OperationState.Cancelling
                    ? await UndoNextAsync(operation, steps, dbContext, secrets, cancellationToken)
                    : await RunCurrentAsync(operation, steps, dbContext, secrets, cancellationToken);

                foreach (var action in happened)
                {
                    audit.Record(action, Operations.Describe(operation), Details(operation, step));
                }

                operation.ExtendLease(clock.GetUtcNow() + options.Lease);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            operation.ReleaseLease();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else has it now: a worker that took over once this one's lease ran out, or a person's retry or
            // cancel. What this run found is not written down; the step is asked again and finds what it made.
            logger.LogWarning("Operation {OperationId} changed under this worker; its current step will be asked again.", operationId);
        }

        return true;
    }

    private async Task<(ProvisioningStep? Step, IReadOnlyList<string> Happened)> RunCurrentAsync(
        ProvisioningOperation operation, List<IOperationStep> steps, DbContext dbContext, ISecretStore secrets, CancellationToken cancellationToken)
    {
        if (operation.Current is not { } current)
        {
            return (null, []);
        }

        current.Start(clock.GetUtcNow());

        var step = steps.SingleOrDefault(candidate => candidate.Name == current.Name);
        var outcome = step is null
            ? StepOutcome.Failed($"This build has no step '{current.Name}' for '{operation.Kind}'.")
            : await RunStepAsync(step, new StepRun(operation, current, dbContext, secrets, clock), cancellationToken);
        var data = outcome.Data is null ? null : JsonSerializer.Serialize(outcome.Data, Operations.Json);

        return (current, operation.Record(current, outcome, data, clock.GetUtcNow(), options));
    }

    // Takes away what the operation made, newest first, and only that: something it found is not its to remove, and a
    // step that says its resource is not the kind taken back (a purchased domain, a mailbox) keeps it.
    private async Task<(ProvisioningStep? Step, IReadOnlyList<string> Happened)> UndoNextAsync(
        ProvisioningOperation operation, List<IOperationStep> steps, DbContext dbContext, ISecretStore secrets, CancellationToken cancellationToken)
    {
        if (operation.NextToUndo is not { } made)
        {
            return (null, operation.CompleteCancel(clock.GetUtcNow()));
        }

        var step = steps.SingleOrDefault(candidate => candidate.Name == made.Name);

        if (step is not { CanUndo: true })
        {
            return (made, operation.RecordUndone(made, removed: false, clock.GetUtcNow()));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StepTimeout);

        try
        {
            await step.UndoAsync(new StepRun(operation, made, dbContext, secrets, clock), timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Undoing step {Step} of operation {OperationId} failed.", made.Name, operation.Id);

            return (made, operation.RecordUndoFailure(made, Describe(exception), clock.GetUtcNow(), options));
        }

        if (made.SecretFor is not null)
        {
            await secrets.ForgetAsync(made.SecretName, cancellationToken);
            made.ForgetSecret();
        }

        return (made, operation.RecordUndone(made, removed: true, clock.GetUtcNow()));
    }

    // A step that throws, or does not answer in time, may or may not have reached the provider. Either way it is tried
    // again, and asks before it makes. Only this worker's own stopping is let through, and then nothing is written.
    private async Task<StepOutcome> RunStepAsync(IOperationStep step, StepRun context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StepTimeout);

        try
        {
            return await step.RunAsync(context, timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Step {Step} of operation {OperationId} failed on attempt {Attempt}.", step.Name, context.OperationId, context.Attempts + 1);

            return StepOutcome.Retry(exception is OperationCanceledException
                ? $"No answer within {options.StepTimeout.TotalSeconds:0} seconds."
                : Describe(exception));
        }
    }

    private async Task<bool> ClaimAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var now = clock.GetUtcNow();

        var claimed = await Due(scope.ServiceProvider.GetRequiredService<DbContext>(), now)
            .Where(operation => operation.Id == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.LeaseToken, Guid.NewGuid())
                    .SetProperty(operation => operation.LeaseUntil, now + options.Lease)
                    .SetProperty(operation => operation.LeasedBy, RunSchedule.Replica),
                cancellationToken);

        return claimed == 1;
    }

    private async Task<List<Guid>> DueAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        return await Due(scope.ServiceProvider.GetRequiredService<DbContext>(), clock.GetUtcNow())
            .OrderBy(operation => operation.NextCheckAt)
            .Select(operation => operation.Id)
            .Take(Batch)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<ProvisioningOperation> Due(DbContext dbContext, DateTimeOffset now) =>
        dbContext.Set<ProvisioningOperation>()
            .IgnoreQueryFilters()
            .Where(operation =>
                (operation.State == OperationState.Running || operation.State == OperationState.Cancelling)
                && operation.NextCheckAt <= now
                && (operation.LeaseUntil == null || operation.LeaseUntil <= now));

    private bool IsDue(ProvisioningOperation operation) =>
        (operation.State is OperationState.Running or OperationState.Cancelling) && operation.NextCheckAt <= clock.GetUtcNow();

    // What the audit trail keeps of a step: the provider's ids and what it said, never a secret.
    private static object Details(ProvisioningOperation operation, ProvisioningStep? step) => step is null
        ? new { operation = operation.Id }
        : new
        {
            operation = operation.Id,
            step = step.Name,
            requestId = step.RequestId,
            resourceId = step.ResourceId,
            attempts = step.Attempts,
            note = step.Note,
            error = step.LastError,
        };

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";
}

internal sealed class OperationWorker(OperationRunner runner, ProvisioningOptions options, SchemaGate schema, TimeProvider clock, ILogger<OperationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        // The operations are a table, so nothing is claimed before the schema has caught up (D-205).
        await schema.WaitAsync(stoppingToken);

        using var timer = new PeriodicTimer(options.Interval, clock);

        do
        {
            try
            {
                await runner.RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "The provisioning run failed; the next one picks the operations up again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
