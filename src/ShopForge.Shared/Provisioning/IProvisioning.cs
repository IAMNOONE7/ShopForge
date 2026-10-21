namespace ShopForge.Shared.Provisioning;

// Starts work that outlives a request: a launch, a domain, a mailbox, each an operation of persisted steps that a
// worker carries through, survives a restart and never does twice (D-210). The caller gets the operation's id at once
// and nobody waits for DNS.
public interface IProvisioning
{
    // In the current store. A subject (a host name, say) is worked on by one operation across the platform, and a store
    // runs one operation with no subject at a time: while another is unfinished, that one is answered instead (D-212).
    Task<OperationStart> StartAsync(string kind, string? subject, object? input, CancellationToken cancellationToken);

    // Asks an unfinished operation of the current store to stop and take away what it made, as a person's cancel does.
    // Another store's is not touched, and one with a step running is not asked yet: whoever asks, asks again later.
    Task CancelAsync(Guid operationId, CancellationToken cancellationToken);

    // The current store's unfinished operation on a subject, if it has one.
    Task<RunningOperation?> UnfinishedAsync(string subject, CancellationToken cancellationToken);
}

public sealed record OperationStart(Guid OperationId, bool Started);

public sealed record RunningOperation(Guid OperationId, string Kind);

// What an operation is made of: its steps, by name, in the order they run.
public interface IOperationKind
{
    string Name { get; }

    IReadOnlyList<string> Steps { get; }
}
