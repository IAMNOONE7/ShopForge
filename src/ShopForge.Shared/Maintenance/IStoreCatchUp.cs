namespace ShopForge.Shared.Maintenance;

// Work a module needs doing again soon rather than once a day. Housekeeping deletes rows nobody will come back
// for and can wait until tonight; this finishes things somebody is still waiting for, so it runs on a short
// period of its own (D-176).
//
// The worker runs it inside each store's scope, exactly as it runs the daily housekeeping.
public interface IStoreCatchUp
{
    string Name { get; }

    // How many things this pass settled, for the log. Nothing is removed here, which is why it is not the
    // same seam as the housekeeping that counts what it deleted.
    Task<int> RunAsync(CancellationToken cancellationToken);
}
