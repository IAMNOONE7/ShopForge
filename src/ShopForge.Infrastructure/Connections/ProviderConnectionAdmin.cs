using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Connections;

// What a merchant can see and change about the accounts their storefront takes money through. The credential
// itself goes in through one door and comes out of none: nothing here returns it, and `ConnectedProvider` has
// nowhere to put one (D-139).
public sealed class ProviderConnectionAdmin(
    DbContext dbContext,
    IStoreContext storeContext,
    ISecretStore secrets,
    IAuditLog audit,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<ConnectedProvider>> AllAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<StoreProviderConnection>()
            .AsNoTracking()
            .OrderBy(connection => connection.Provider)
            .Select(connection => new ConnectedProvider(
                connection.Provider,
                connection.MerchantId,
                connection.Environment,
                connection.IsActive,
                connection.SecretName != null,
                connection.ChangedAt))
            .ToListAsync(cancellationToken);

    public async Task<ConnectedProvider> SaveAsync(
        string provider,
        string merchantId,
        ProviderEnvironment environment,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var connection = await FindAsync(provider, cancellationToken);

        if (connection is null)
        {
            connection = new StoreProviderConnection(storeContext.StoreId!.Value, provider, merchantId, environment, now);
            dbContext.Add(connection);
        }
        else
        {
            connection.Update(merchantId, environment, isActive, now);
        }

        audit.Record("provider.connected", provider, new { MerchantId = merchantId, Environment = environment.ToString(), IsActive = isActive });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Describe(connection);
    }

    // The credential is filed under a name derived from the store and the provider, so nothing a caller sends
    // decides where it lands or whose it overwrites.
    public async Task<SecretOutcome> SetSecretAsync(string provider, string secret, CancellationToken cancellationToken)
    {
        if (await FindAsync(provider, cancellationToken) is not { } connection)
        {
            return SecretOutcome.NoSuchConnection;
        }

        var name = $"store-{storeContext.StoreId!.Value:n}-{connection.Provider}";

        try
        {
            await secrets.SetAsync(name, secret, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return SecretOutcome.NowhereToKeepIt;
        }

        connection.KeepsSecretAt(name, clock.GetUtcNow());
        audit.Record("provider.secret-set", connection.Provider);
        await dbContext.SaveChangesAsync(cancellationToken);

        return SecretOutcome.Kept;
    }

    public async Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken)
    {
        if (await FindAsync(provider, cancellationToken) is not { } connection)
        {
            return false;
        }

        if (connection.SecretName is { Length: > 0 } name)
        {
            await secrets.ForgetAsync(name, cancellationToken);
        }

        dbContext.Remove(connection);
        audit.Record("provider.disconnected", connection.Provider);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private Task<StoreProviderConnection?> FindAsync(string provider, CancellationToken cancellationToken)
    {
        var key = provider.Trim().ToLowerInvariant();

        return dbContext.Set<StoreProviderConnection>().SingleOrDefaultAsync(candidate => candidate.Provider == key, cancellationToken);
    }

    private static ConnectedProvider Describe(StoreProviderConnection connection) => new(
        connection.Provider,
        connection.MerchantId,
        connection.Environment,
        connection.IsActive,
        connection.SecretName is not null,
        connection.ChangedAt);
}

// HasSecret, never the secret.
public sealed record ConnectedProvider(
    string Provider,
    string MerchantId,
    ProviderEnvironment Environment,
    bool IsActive,
    bool HasSecret,
    DateTimeOffset ChangedAt);

public enum SecretOutcome
{
    Kept,
    NoSuchConnection,
    NowhereToKeepIt,
}
