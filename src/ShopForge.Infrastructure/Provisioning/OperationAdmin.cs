using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;

namespace ShopForge.Infrastructure.Provisioning;

// A store's operations as its admin sees them: every step with its state and its last error, and what a person can do
// about one that stopped (D-210). Nothing here runs a step; it only tells the worker what to do next. A step that is
// running is not interrupted: the action is refused until the worker has let go.
public sealed class OperationAdmin(DbContext dbContext, IAuditLog audit, TimeProvider clock)
{
    private const int Listed = 50;

    public async Task<IReadOnlyList<OperationSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var operations = await dbContext.Set<ProvisioningOperation>()
            .AsNoTracking()
            .Include(operation => operation.Steps)
            .OrderByDescending(operation => operation.CreatedAt)
            .Take(Listed)
            .ToListAsync(cancellationToken);

        return [.. operations.Select(operation => new OperationSummary(
            operation.Id,
            operation.Kind,
            operation.Subject,
            operation.State,
            operation.Current?.Name,
            operation.CreatedAt,
            operation.FinishedAt))];
    }

    public async Task<OperationDetail?> FindAsync(Guid operationId, CancellationToken cancellationToken) =>
        await LoadAsync(operationId, tracked: false, cancellationToken) is { } operation ? Describe(operation) : null;

    public Task<OperationActionResult> RetryAsync(Guid operationId, CancellationToken cancellationToken) =>
        ActAsync(operationId, "retry", operation => operation.CanRetry, operation => operation.Retry(clock.GetUtcNow()), "operation.retried", cancellationToken);

    public Task<OperationActionResult> ContinueAsync(Guid operationId, CancellationToken cancellationToken) =>
        ActAsync(operationId, "continue", operation => operation.CanContinue, operation => operation.Continue(clock.GetUtcNow()), "operation.continued", cancellationToken);

    public Task<OperationActionResult> CancelAsync(Guid operationId, CancellationToken cancellationToken) =>
        ActAsync(operationId, "cancel", operation => operation.CanCancel, operation => operation.RequestCancel(clock.GetUtcNow()), "operation.cancel-requested", cancellationToken);

    private async Task<OperationActionResult> ActAsync(
        Guid operationId,
        string action,
        Func<ProvisioningOperation, bool> allowed,
        Action<ProvisioningOperation> act,
        string recorded,
        CancellationToken cancellationToken)
    {
        if (await LoadAsync(operationId, tracked: true, cancellationToken) is not { } operation)
        {
            return OperationActionResult.NotFound;
        }

        if (operation.IsLeased(clock.GetUtcNow()))
        {
            return OperationActionResult.Refused($"A step of this operation is running; {action} it once it has stopped.");
        }

        if (!allowed(operation))
        {
            return OperationActionResult.Refused($"An operation that is {operation.State} cannot be told to {action}.");
        }

        act(operation);
        audit.Record(recorded, Operations.Describe(operation), new { operation = operation.Id });

        // The row's version is checked, so a worker that claimed it a moment ago wins and this is refused instead.
        await dbContext.SaveChangesAsync(cancellationToken);

        return OperationActionResult.Done(Describe(operation));
    }

    private Task<ProvisioningOperation?> LoadAsync(Guid operationId, bool tracked, CancellationToken cancellationToken)
    {
        var operations = dbContext.Set<ProvisioningOperation>().Include(operation => operation.Steps);

        return (tracked ? operations : operations.AsNoTracking())
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
    }

    private OperationDetail Describe(ProvisioningOperation operation) => new(
        operation.Id,
        operation.Kind,
        operation.Subject,
        operation.State,
        operation.IsLeased(clock.GetUtcNow()),
        operation.CreatedAt,
        operation.ChangedAt,
        operation.FinishedAt,
        operation.NextCheckAt,
        [
            .. new[]
            {
                (Name: "retry", Allowed: operation.CanRetry),
                (Name: "continue", Allowed: operation.CanContinue),
                (Name: "cancel", Allowed: operation.CanCancel),
            }.Where(candidate => candidate.Allowed).Select(candidate => candidate.Name),
        ],
        [
            .. operation.Steps.Select(step => new OperationStepView(
                step.Name,
                step.Status,
                step.ResourceId,
                step.Created,
                step.RequestId,
                step.SecretFor is not null,
                step.Attempts,
                step.Checks,
                step.WaitingSince,
                step.Note,
                step.LastError,
                step.LastErrorAt,
                step.StartedAt,
                step.FinishedAt)),
        ]);
}

public sealed record OperationSummary(
    Guid Id, string Kind, string? Subject, OperationState State, string? CurrentStep, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);

public sealed record OperationDetail(
    Guid Id,
    string Kind,
    string? Subject,
    OperationState State,
    bool StepRunning,
    DateTimeOffset CreatedAt,
    DateTimeOffset ChangedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? NextCheckAt,
    IReadOnlyList<string> Actions,
    IReadOnlyList<OperationStepView> Steps);

// Whether the step keeps a secret, never the secret.
public sealed record OperationStepView(
    string Name,
    StepStatus Status,
    string? ResourceId,
    bool Created,
    string? RequestId,
    bool KeepsSecret,
    int Attempts,
    int Checks,
    DateTimeOffset? WaitingSince,
    string? Note,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record OperationActionResult(bool Found, string? Refusal, OperationDetail? Operation)
{
    public static OperationActionResult NotFound { get; } = new(false, null, null);

    public static OperationActionResult Refused(string reason) => new(true, reason, null);

    public static OperationActionResult Done(OperationDetail operation) => new(true, null, operation);
}
