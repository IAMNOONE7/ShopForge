namespace ShopForge.Shared.Access;

// A company is only usable once somebody can sign in for it, and tenant users belong to Access (D-106). The first
// owner is invited rather than given a password the platform chose (D-114), so the objection has to come before the
// company exists: an invitation that cannot be accepted would leave a tenant nobody can reach.
public interface ITenantInitializer
{
    Task<string?> FindProblemAsync(NewTenantOwner owner, CancellationToken cancellationToken);

    Task InitializeAsync(Guid tenantId, NewTenantOwner owner, CancellationToken cancellationToken);
}

public sealed record NewTenantOwner(string Email, string InvitedBy);
