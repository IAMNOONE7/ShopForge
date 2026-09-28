namespace ShopForge.Shared.Maintenance;

// Housekeeping for rows no store owns: an invitation into a company, a platform operator's spent link, the
// outbox, the audit trail. The worker runs these once a day with nothing in scope, so a sweep sees every row
// rather than one store's (D-118).
public interface IMaintenanceOutsideStores
{
    string Name { get; }

    Task<int> RunAsync(CancellationToken cancellationToken);
}
