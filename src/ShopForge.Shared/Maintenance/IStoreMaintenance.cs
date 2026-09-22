namespace ShopForge.Shared.Maintenance;

// Housekeeping a module owns: rows nobody will come back for. The worker runs it inside each store's scope, so a
// module's cleanup sees exactly the data its endpoints see.
public interface IStoreMaintenance
{
    string Name { get; }

    Task<int> RunAsync(CancellationToken cancellationToken);
}
