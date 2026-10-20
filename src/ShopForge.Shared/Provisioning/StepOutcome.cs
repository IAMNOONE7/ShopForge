namespace ShopForge.Shared.Provisioning;

// How a step's turn ended. Waiting is not failing: a certificate or a delegation takes minutes to hours, and a step
// that is waiting is looked at again on a backoff of its own without using up any of its tries (D-210).
public sealed record StepOutcome
{
    private StepOutcome(StepOutcomeKind kind, string? message, string? resourceId, bool created, string? requestId, object? data, TimeSpan? checkAgainIn)
    {
        Kind = kind;
        Message = message;
        ResourceId = resourceId;
        Created = created;
        RequestId = requestId;
        Data = data;
        CheckAgainIn = checkAgainIn;
    }

    public StepOutcomeKind Kind { get; }

    // Why it is waiting, what it needs from a person, or what went wrong. Never a secret.
    public string? Message { get; }

    public string? ResourceId { get; }

    // Whether this run made the resource rather than finding it, which is what a cancellation may take away.
    public bool Created { get; }

    // The provider's id for the request, for whoever asks the provider about it.
    public string? RequestId { get; }

    // Handed on to the steps after this one. Never a secret.
    public object? Data { get; }

    public TimeSpan? CheckAgainIn { get; }

    public static StepOutcome Done(string? resourceId = null, bool created = false, string? requestId = null, object? data = null) =>
        new(StepOutcomeKind.Done, null, resourceId, created, requestId, data, null);

    public static StepOutcome Waiting(string reason, TimeSpan? checkAgainIn = null, string? requestId = null) =>
        new(StepOutcomeKind.Waiting, reason, null, false, requestId, null, checkAgainIn);

    public static StepOutcome NeedsInput(string prompt, string? requestId = null) =>
        new(StepOutcomeKind.NeedsInput, prompt, null, false, requestId, null, null);

    // Something that may go away by itself: tried again after a while, until the tries run out.
    public static StepOutcome Retry(string error, string? requestId = null) =>
        new(StepOutcomeKind.Retry, error, null, false, requestId, null, null);

    // Something trying again will not fix: the operation stops here for a person.
    public static StepOutcome Failed(string error, string? requestId = null) =>
        new(StepOutcomeKind.Failed, error, null, false, requestId, null, null);
}

public enum StepOutcomeKind
{
    Done,
    Waiting,
    NeedsInput,
    Retry,
    Failed,
}
