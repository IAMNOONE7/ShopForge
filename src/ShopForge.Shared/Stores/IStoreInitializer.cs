using ShopForge.Shared.Payments;

namespace ShopForge.Shared.Stores;

// Runs when a store is created, so each module can set up the store's own defaults. The store's own row is not
// in the database yet when this runs, so what the store is gets handed over rather than looked up (D-186).
public interface IStoreInitializer
{
    Task InitializeAsync(NewStore store, CancellationToken cancellationToken);
}

public sealed record NewStore(Currency Currency);
