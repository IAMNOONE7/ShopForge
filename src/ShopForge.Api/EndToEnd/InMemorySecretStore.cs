using System.Collections.Concurrent;
using ShopForge.Shared.Security;

namespace ShopForge.Api.EndToEnd;

// Secrets for a deployment that is thrown away after its run: kept in this process and gone with it. Writable,
// because a journey connects a provider through the admin the way a merchant would, and configuration cannot be
// written to (D-203).
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.TryGetValue(SecretNames.Required(name), out var value) ? value : null);

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _secrets[SecretNames.Required(name)] = value;

        return Task.CompletedTask;
    }

    public Task ForgetAsync(string name, CancellationToken cancellationToken)
    {
        _secrets.TryRemove(SecretNames.Required(name), out _);

        return Task.CompletedTask;
    }
}
