namespace ShopForge.Shared.Tenancy;

public interface ITenantOwned
{
    Guid TenantId { get; }
}
