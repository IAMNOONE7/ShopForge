namespace ShopForge.Shared.Provisioning;

// One step of an operation, asked to do its part and say how it went. It may run more than once: after a crash, after
// a timeout that may or may not have reached the provider, or on another replica once the first one's lease ran out.
// So it asks before it makes, under its stable name, and tags what it makes with the operation's id: a step that finds
// what it was about to create takes it rather than making a second one, and knows from the tag whether its own
// operation made it, which is what a cancellation may take away (D-210).
public interface IOperationStep
{
    string Kind { get; }

    string Name { get; }

    // Whether what this step made may be taken away again when its operation is cancelled. A purchased domain or an
    // opened mailbox is not, so the default is to keep it.
    bool CanUndo => false;

    Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken);

    // Removes what this step made, named by the context's resource id. Only called for a resource this operation
    // created, never for one it found.
    Task UndoAsync(IStepContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

// What a step knows about the operation it is part of, and what it has recorded so far.
public interface IStepContext
{
    Guid OperationId { get; }

    Guid StoreId { get; }

    string Kind { get; }

    string Step { get; }

    string? Subject { get; }

    // Failed tries so far, and how many times it has been looked at while waiting, which is not failing.
    int Attempts { get; }

    int Checks { get; }

    DateTimeOffset? WaitingSince { get; }

    // What this step recorded when it last finished, which an undo removes.
    string? ResourceId { get; }

    bool Created { get; }

    // The same for every run of this step in this store, and for every replica, so a retry asks the provider for the
    // thing an earlier run may already have made.
    string StableName { get; }

    T? Input<T>();

    // What an earlier step of the operation handed on when it finished.
    T? Output<T>(string step);

    // Whether this step has kept the secret of exactly that resource. A credential found at the provider without its
    // secret kept here is an orphan: it is revoked and replaced, never recorded as configured.
    bool HoldsSecretFor(string resourceId);

    // Keeps a credential's secret before anything else happens to it, and records whose secret it is.
    Task KeepSecretAsync(string resourceId, string secret, CancellationToken cancellationToken);

    // Where that secret is kept, for whatever reads it once the operation is over. Never the secret.
    string SecretName { get; }

    Task<string?> SecretAsync(CancellationToken cancellationToken);
}
