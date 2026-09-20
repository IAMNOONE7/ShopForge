namespace ShopForge.Shared.Stores;

// Runs when a store is created, so each module can set up the store's own defaults.
public interface IStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
