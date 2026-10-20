using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Provisioning;

// One step of an operation and everything a later run of it needs: what it made or found, the provider's id for the
// request, whose secret it keeps, how often it failed and how long it has been waiting (D-210).
internal sealed class ProvisioningStep : IStoreOwned
{
    public const int MaxNameLength = 100;
    public const int MaxResourceIdLength = 500;
    public const int MaxRequestIdLength = 200;
    public const int MaxNoteLength = 1000;

    private ProvisioningStep()
    {
    }

    public ProvisioningStep(Guid operationId, Guid storeId, int position, string name)
    {
        Id = Guid.CreateVersion7();
        OperationId = operationId;
        StoreId = storeId;
        Position = position;
        Name = name;
        Status = StepStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid OperationId { get; private set; }

    public Guid StoreId { get; private set; }

    public int Position { get; private set; }

    public string Name { get; private set; } = null!;

    public StepStatus Status { get; private set; }

    public string? ResourceId { get; private set; }

    // Whether this operation made the resource rather than finding it. Only what it made is taken away on cancelling.
    public bool Created { get; private set; }

    public string? RequestId { get; private set; }

    // What the step handed on to the ones after it, as JSON. Never a secret.
    public string? Data { get; private set; }

    // The resource whose secret this step keeps, under SecretName. A credential with any other id is an orphan.
    public string? SecretFor { get; private set; }

    public int Attempts { get; private set; }

    public int Checks { get; private set; }

    public DateTimeOffset? WaitingSince { get; private set; }

    // Why it is waiting, or what it needs from a person.
    public string? Note { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? LastErrorAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    // One per step of one operation, so a retry writes over what an earlier run of it kept.
    public string SecretName => $"provisioning-{OperationId:N}-{Position}";

    public void Start(DateTimeOffset now) => StartedAt ??= now;

    public void Succeed(string? resourceId, bool created, string? requestId, string? data, DateTimeOffset now)
    {
        Status = StepStatus.Succeeded;
        ResourceId = Trim(resourceId, MaxResourceIdLength);
        Created = created;
        RequestId = Trim(requestId, MaxRequestIdLength) ?? RequestId;
        Data = data;
        Note = null;
        WaitingSince = null;
        FinishedAt = now;
    }

    public void Wait(string? reason, string? requestId, DateTimeOffset now)
    {
        Status = StepStatus.Waiting;
        Checks++;
        WaitingSince ??= now;
        Note = Trim(reason, MaxNoteLength);
        RequestId = Trim(requestId, MaxRequestIdLength) ?? RequestId;
    }

    public void AwaitInput(string? prompt, string? requestId, DateTimeOffset now)
    {
        Status = StepStatus.AwaitingInput;
        Note = Trim(prompt, MaxNoteLength);
        RequestId = Trim(requestId, MaxRequestIdLength) ?? RequestId;
        WaitingSince ??= now;
    }

    public void Fail(string? error, string? requestId, DateTimeOffset now, bool givingUp)
    {
        Attempts++;
        LastError = Trim(error, MaxNoteLength);
        LastErrorAt = now;
        RequestId = Trim(requestId, MaxRequestIdLength) ?? RequestId;

        if (givingUp)
        {
            Status = StepStatus.Failed;
            FinishedAt = now;
        }
    }

    public void Undo(bool removed, DateTimeOffset now)
    {
        Status = removed ? StepStatus.Undone : StepStatus.Kept;
        FinishedAt = now;
    }

    // The resource is still there; the step stays as it was until the clean-up is tried again.
    public void FailUndo(string error, DateTimeOffset now, bool givingUp)
    {
        Attempts++;
        LastError = Trim(error, MaxNoteLength);
        LastErrorAt = now;

        if (givingUp)
        {
            FinishedAt = now;
        }
    }

    public void Reopen()
    {
        Status = StepStatus.Pending;
        Attempts = 0;
        Note = null;
        FinishedAt = null;
    }

    public void ResetAttempts() => Attempts = 0;

    public void KeepSecretFor(string resourceId) => SecretFor = Trim(resourceId, MaxResourceIdLength);

    public void ForgetSecret() => SecretFor = null;

    private static string? Trim(string? value, int length) => value is { Length: var actual } && actual > length ? value[..length] : value;
}

public enum StepStatus
{
    Pending,
    Waiting,
    AwaitingInput,
    Succeeded,
    Failed,
    Undone,
    Kept,
}
