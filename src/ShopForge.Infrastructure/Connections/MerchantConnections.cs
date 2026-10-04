using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Connections;

// The one read of these rows that deliberately leaves its store behind. It answers with store ids and nothing
// else: a caller learns which stores to look in, never a merchant's credentials, and a connection with no secret
// behind it is left out because a callback for it could not be verified anyway.
internal sealed class MerchantConnections(DbContext dbContext) : IMerchantConnections
{
    public async Task<IReadOnlyList<Guid>> StoresAsync(string provider, string merchantId, CancellationToken cancellationToken)
    {
        var key = provider.Trim().ToLowerInvariant();

        return await dbContext.Set<StoreProviderConnection>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .AsNoTracking()
            .Where(connection => connection.Provider == key
                && connection.MerchantId == merchantId
                && connection.IsActive
                && connection.SecretName != null)
            .Select(connection => connection.StoreId)
            .ToListAsync(cancellationToken);
    }
}
