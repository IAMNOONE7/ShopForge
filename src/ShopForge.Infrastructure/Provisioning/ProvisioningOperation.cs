using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Provisioning;

// Work that outlives a request, kept as rows: an operation, its steps in order, and how far each has got (D-210).
// Its steps run one at a time, so the lease that stops two replicas running one step is held on the operation: a
// worker claims it, runs its current step, writes down what happened and lets go. A worker that dies holding it
// writes nothing, and once the lease runs out another takes the step from where the rows say it stood.
internal sealed class ProvisioningOperation : IStoreOwned
{
    public const int MaxKindLength = 100;
    public const int MaxSubjectLength = 253;

    private readonly List<ProvisioningStep> _steps = [];

    private ProvisioningOperation()
    {
    }

    public ProvisioningOperation(Guid tenantId, Guid storeId, string kind, string? subject, string? input, IEnumerable<string> steps, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        StoreId = storeId;
        Kind = kind;
        Subject = subject;
        Input = input;
        State = OperationState.Running;
        CreatedAt = now;
        ChangedAt = now;
        NextCheckAt = now;

        var position = 0;

        foreach (var name in steps)
        {
            _steps.Add(new ProvisioningStep(Id, storeId, position++, name));
        }
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public string Kind { get; private set; } = null!;

    // What the operation works on across the platform, a host name say, which no other unfinished operation may hold.
    public string? Subject { get; private set; }

    // What it was started with, as JSON. Never a secret.
    public string? Input { get; private set; }

    public OperationState State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    // Set once it has nothing left to do, which is what lets the store and the subject start another.
    public DateTimeOffset? FinishedAt { get; private set; }

    // When a worker should next look at it. None while it waits for a person.
    public DateTimeOffset? NextCheckAt { get; private set; }

    public DateTimeOffset? CancelRequestedAt { get; private set; }

    public string? LeasedBy { get; private set; }

    public DateTimeOffset? LeaseUntil { get; private set; }

    public Guid? LeaseToken { get; private set; }

    public IReadOnlyList<ProvisioningStep> Steps => [.. _steps.OrderBy(step => step.Position)];

    // The step whose turn it is: the first that has not succeeded.
    public ProvisioningStep? Current => Steps.FirstOrDefault(step => step.Status != StepStatus.Succeeded);

    // What a cancellation still has to take away, newest first: only what this operation made.
    public ProvisioningStep? NextToUndo => Steps.LastOrDefault(step => step is { Status: StepStatus.Succeeded, Created: true });

    public bool IsLeased(DateTimeOffset now) => LeaseUntil > now;

    // Writes down how the current step's turn went, and returns what happened for the audit trail.
    public IReadOnlyList<string> Record(ProvisioningStep step, StepOutcome outcome, string? data, DateTimeOffset now, ProvisioningOptions options)
    {
        ChangedAt = now;

        switch (outcome.Kind)
        {
            case StepOutcomeKind.Done:
                step.Succeed(outcome.ResourceId, outcome.Created, outcome.RequestId, data, now);

                if (Current is null)
                {
                    State = OperationState.Succeeded;
                    FinishedAt = now;
                    NextCheckAt = null;

                    return ["operation.step-succeeded", "operation.succeeded"];
                }

                NextCheckAt = now;

                return ["operation.step-succeeded"];

            case StepOutcomeKind.Waiting:
                var started = step.Status != StepStatus.Waiting;
                step.Wait(outcome.Message, outcome.RequestId, now);
                NextCheckAt = now + options.CheckAfter(step.Checks, outcome.CheckAgainIn);

                return started ? ["operation.step-waiting"] : [];

            case StepOutcomeKind.NeedsInput:
                step.AwaitInput(outcome.Message, outcome.RequestId, now);
                State = OperationState.AwaitingInput;
                NextCheckAt = null;

                return ["operation.step-awaiting-input"];

            case StepOutcomeKind.Retry when step.Attempts + 1 < options.MaxAttempts:
                step.Fail(outcome.Message, outcome.RequestId, now, givingUp: false);
                NextCheckAt = now + options.RetryAfter(step.Attempts);

                return ["operation.step-retrying"];

            default:
                step.Fail(outcome.Message, outcome.RequestId, now, givingUp: true);
                State = OperationState.Failed;
                NextCheckAt = null;

                return ["operation.step-failed"];
        }
    }

    // A cancellation's step done: what the step made is gone, or kept because it is not the kind of thing taken back.
    public IReadOnlyList<string> RecordUndone(ProvisioningStep step, bool removed, DateTimeOffset now)
    {
        ChangedAt = now;
        step.Undo(removed, now);
        NextCheckAt = now;

        return [removed ? "operation.step-undone" : "operation.step-kept"];
    }

    // Nothing this operation made is left, so the store and the subject are free again.
    public IReadOnlyList<string> CompleteCancel(DateTimeOffset now)
    {
        State = OperationState.Cancelled;
        ChangedAt = now;
        FinishedAt = now;
        NextCheckAt = null;

        return ["operation.cancelled"];
    }

    public IReadOnlyList<string> RecordUndoFailure(ProvisioningStep step, string error, DateTimeOffset now, ProvisioningOptions options)
    {
        ChangedAt = now;
        var givingUp = step.Attempts + 1 >= options.MaxAttempts;
        step.FailUndo(error, now, givingUp);

        if (givingUp)
        {
            State = OperationState.Failed;
            NextCheckAt = null;

            return ["operation.step-undo-failed"];
        }

        NextCheckAt = now + options.RetryAfter(step.Attempts);

        return ["operation.step-undo-retrying"];
    }

    public bool CanRetry => State == OperationState.Failed;

    public bool CanContinue => State == OperationState.AwaitingInput;

    public bool CanCancel => (State is OperationState.Running or OperationState.AwaitingInput or OperationState.Failed) && CancelRequestedAt is null;

    // Resumes from where it stopped: the step that gave up goes again with its tries back, or, for an operation being
    // cancelled, the clean-up that gave up does.
    public void Retry(DateTimeOffset now)
    {
        if (CancelRequestedAt is not null)
        {
            NextToUndo?.ResetAttempts();
            State = OperationState.Cancelling;
        }
        else
        {
            Current?.Reopen();
            State = OperationState.Running;
        }

        ChangedAt = now;
        NextCheckAt = now;
    }

    public void Continue(DateTimeOffset now)
    {
        Current?.Reopen();
        State = OperationState.Running;
        ChangedAt = now;
        NextCheckAt = now;
    }

    public void RequestCancel(DateTimeOffset now)
    {
        // The step it stopped at is no longer waiting for anything or anybody.
        if (Current is { Status: StepStatus.Waiting or StepStatus.AwaitingInput or StepStatus.Failed } stopped)
        {
            stopped.Reopen();
        }

        foreach (var step in Steps.Where(candidate => candidate is { Status: StepStatus.Succeeded, Created: true }))
        {
            step.ResetAttempts();
        }

        CancelRequestedAt = now;
        State = OperationState.Cancelling;
        ChangedAt = now;
        NextCheckAt = now;
    }

    public void ExtendLease(DateTimeOffset until) => LeaseUntil = until;

    public void ReleaseLease()
    {
        LeasedBy = null;
        LeaseUntil = null;
        LeaseToken = null;
    }

    public void Touch(DateTimeOffset now) => ChangedAt = now;
}

public enum OperationState
{
    Running,
    AwaitingInput,
    Failed,
    Succeeded,
    Cancelling,
    Cancelled,
}
