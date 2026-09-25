namespace ShopForge.Shared.Platform;

// What the current tenant's plan allows. The module that creates the thing compares the cap with its own count, so
// the plan lives in one place and no module reads another's tables to enforce it (D-110).
public interface ITenantLimits
{
    Task<int?> MaxAsync(TenantResource resource, CancellationToken cancellationToken);
}

public enum TenantResource
{
    Stores,
    Products,
}
