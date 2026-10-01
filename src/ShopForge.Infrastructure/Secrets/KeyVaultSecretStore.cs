using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Secrets;

// A merchant's own credentials, kept where the deployment's are kept but read on demand rather than at startup:
// a connection made this afternoon has to work this afternoon, and a rotated secret has to stop working at once
// (D-139).
//
// A lookup is a network call, and a gateway's secret is needed on the path a shopper is waiting on, so what is
// found is held briefly. Writing or forgetting drops the held copy, which is what makes rotation immediate for
// the instance that did it; the others let their copy expire. Nothing here is logged — the value is the one
// thing in the system that must never reach a log line, and the way to be sure is for no line to mention it.
internal sealed class KeyVaultSecretStore(SecretClient client, IMemoryCache cache) : ISecretStore
{
    private static readonly TimeSpan HeldFor = TimeSpan.FromMinutes(5);

    public async Task<string?> FindAsync(string name, CancellationToken cancellationToken)
    {
        var key = CacheKey(SecretNames.Required(name));

        if (cache.TryGetValue<string?>(key, out var held))
        {
            return held;
        }

        var found = await ReadAsync(name, cancellationToken);
        cache.Set(key, found, HeldFor);

        return found;
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        await client.SetSecretAsync(SecretNames.Required(name), value, cancellationToken);
        cache.Remove(CacheKey(name));
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

        cache.Remove(CacheKey(name));
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
