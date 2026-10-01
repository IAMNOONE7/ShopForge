using System.Collections.Concurrent;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// A secret store that can be written to. The deployment default reads configuration and refuses to write, which
// is right and is covered by its own unit test; a vault is what a real deployment uses and there is none here,
// so this stands in for one (D-152).
internal sealed class InMemorySecrets : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _kept = new(StringComparer.Ordinal);

    public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_kept.GetValueOrDefault(SecretNames.Required(name)));

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        _kept[SecretNames.Required(name)] = value;

        return Task.CompletedTask;
    }

    public Task ForgetAsync(string name, CancellationToken cancellationToken)
    {
        _kept.TryRemove(SecretNames.Required(name), out _);

        return Task.CompletedTask;
    }
}
