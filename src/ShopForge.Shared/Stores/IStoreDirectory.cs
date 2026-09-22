namespace ShopForge.Shared.Stores;

// Background work runs outside a request and has no store context to inherit; this is how it enumerates stores to scope itself to.
public interface IStoreDirectory
{
    Task<IReadOnlyList<StoreReference>> AllAsync(CancellationToken cancellationToken);

    Task<StoreReference?> FindAsync(Guid storeId, CancellationToken cancellationToken);
}

public sealed record StoreReference(Guid StoreId, Guid TenantId);
