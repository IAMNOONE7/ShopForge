namespace ShopForge.Shared.Access;

// A company is only usable once somebody can sign in for it, and tenant users belong to Access (D-106).
public interface ITenantInitializer
{
    Task InitializeAsync(Guid tenantId, NewTenantOwner owner, CancellationToken cancellationToken);
}

public sealed record NewTenantOwner(string Email, string Password);
