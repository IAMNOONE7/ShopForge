namespace ShopForge.Shared.Privacy;

// What one module holds about one customer of one store, and how it lets go of it. Each module answers for its own
// rows inside the store's scope, so nothing reads another module's tables to assemble an export or to erase
// somebody — the shape `ITenantUsage` already established (D-105, D-117).
public interface ICustomerData
{
    Task<IReadOnlyList<CustomerDataSection>> ExportAsync(Guid storeCustomerId, CancellationToken cancellationToken);

    // An eraser may save as it goes, because some of what it does has to be visible to the next query it makes.
    // The caller holds a transaction around all of them, so a partial erasure is never committed.
    Task EraseAsync(Guid storeCustomerId, CancellationToken cancellationToken);
}

public sealed record CustomerDataSection(string Name, IReadOnlyList<object> Entries);
