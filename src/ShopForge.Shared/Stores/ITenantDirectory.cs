namespace ShopForge.Shared.Stores;

// Whether a company may use the platform at all. Suspension closes its shops and its admin, so the modules that
// authenticate ask here rather than reading the tenant themselves (D-104).
public interface ITenantDirectory
{
    Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken);
}
