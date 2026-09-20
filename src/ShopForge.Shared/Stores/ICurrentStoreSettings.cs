namespace ShopForge.Shared.Stores;

public interface ICurrentStoreSettings
{
    Task<StoreSettings> GetAsync(CancellationToken cancellationToken);
}

public sealed record StoreSettings(string Name, string Currency, string Culture);
