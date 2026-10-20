namespace ShopForge.Shared.Provisioning;

// Starts work that outlives a request: a launch, a domain, a mailbox, each an operation of persisted steps that a
// worker carries through, survives a restart and never does twice (D-210). The caller gets the operation's id at once
// and nobody waits for DNS.
public interface IProvisioning
{
    // In the current store. A store runs one operation at a time, and a subject (a host name, say) is worked on by one
    // operation across the platform: while another is unfinished, that one is answered instead.
    Task<OperationStart> StartAsync(string kind, string? subject, object? input, CancellationToken cancellationToken);
}

public sealed record OperationStart(Guid OperationId, bool Started);

// What an operation is made of: its steps, by name, in the order they run.
public interface IOperationKind
{
    string Name { get; }

    IReadOnlyList<string> Steps { get; }
}
