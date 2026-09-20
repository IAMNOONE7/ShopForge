namespace ShopForge.Shared.Stores;

// Each module answers whether the current store is ready to go live; a null result means nothing is missing.
public interface IStorePublishCheck
{
    Task<string?> FindProblemAsync(CancellationToken cancellationToken);
}
