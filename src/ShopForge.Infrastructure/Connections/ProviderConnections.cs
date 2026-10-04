using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;

namespace ShopForge.Infrastructure.Connections;

// Answers for the store in scope and no other: the rows are store-owned, so the tenancy filter decides which
// connection a provider can even see (D-004). A credential resolved for the wrong store would send a payment to
// the wrong merchant, which is why nothing here takes a store id as an argument.
internal sealed class ProviderConnections(DbContext dbContext) : IProviderConnections
{
    public async Task<ProviderConnection?> FindAsync(string provider, CancellationToken cancellationToken)
    {
        var key = provider.Trim().ToLowerInvariant();

        return await dbContext.Set<StoreProviderConnection>()
            .AsNoTracking()
            .Where(connection => connection.Provider == key && connection.IsActive)
            .Select(connection => new ProviderConnection(
                connection.Provider,
                connection.MerchantId,
                connection.Environment,
                connection.SecretName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ConnectedAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<StoreProviderConnection>()
            .AsNoTracking()
            .Where(connection => connection.IsActive && connection.SecretName != null)
            .Select(connection => connection.Provider)
            .ToListAsync(cancellationToken);
}
