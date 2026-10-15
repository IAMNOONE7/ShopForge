using Azure;
using Azure.Security.KeyVault.Secrets;
using ShopForge.Shared.Caching;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Secrets;

// A merchant's own credentials, kept where the deployment's are kept but read on demand rather than at startup:
// a connection made this afternoon has to work this afternoon, and a rotated secret has to stop working at once
// (D-139).
//
// A lookup is a network call, and a gateway's secret is needed on the path a shopper is waiting on, so what is
// found is held briefly — what is not found too. Writing or forgetting drops the held copy on every replica at
// once, so a rotated or revoked credential stops being used everywhere within moments rather than when each copy
// expires, and a connection made on one replica is usable on the next (D-202). Nothing here is logged — the value
// is the one thing in the system that must never reach a log line, and the way to be sure is for no line to
// mention it.
internal sealed class KeyVaultSecretStore(SecretClient client, IReplicaCache cache) : ISecretStore
{
    private static readonly TimeSpan HeldFor = TimeSpan.FromMinutes(5);

    public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
        cache.GetAsync(CacheKey(SecretNames.Required(name)), HeldFor, reading => ReadAsync(name, reading), _ => true, cancellationToken);

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        await client.SetSecretAsync(SecretNames.Required(name), value, cancellationToken);
        await cache.ForgetAsync([CacheKey(name)], cancellationToken);
    }

    public async Task ForgetAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await client.StartDeleteSecretAsync(SecretNames.Required(name), cancellationToken);
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            // Already gone, which is the state the caller asked for.
        }

        await cache.ForgetAsync([CacheKey(name)], cancellationToken);
    }

    private async Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return (await client.GetSecretAsync(name, cancellationToken: cancellationToken)).Value.Value;
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            // A reference pointing at nothing is not an error here: the caller decides what to do without it,
            // and for a provider that means not being offered rather than failing when somebody tries to pay.
            return null;
        }
    }

    private static string CacheKey(string name) => $"secret:{name}";
}
