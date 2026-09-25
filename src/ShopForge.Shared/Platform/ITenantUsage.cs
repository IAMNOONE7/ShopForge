namespace ShopForge.Shared.Platform;

// What a tenant is using, answered by the module that owns the data. The platform adds the answers up; no module
// reads another's tables to do it (D-105).
//
// Store-owned rows carry no tenant of their own — the store they belong to does — so the caller, which owns the
// tenant and its stores, says which stores to count. A module counting tenant-owned rows can ignore them and let
// the tenant filter do the work.
public interface ITenantUsage
{
    Task<IReadOnlyList<UsageCount>> CountAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken);
}

public sealed record UsageCount(string Name, int Value);
