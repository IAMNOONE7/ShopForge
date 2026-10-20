using System.Collections.Concurrent;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// A secret store that can be written to. The deployment default reads configuration and refuses to write, which
// is right and is covered by its own unit test; a vault is what a real deployment uses and there is none here,
// so this stands in for one (D-152).
internal sealed class InMemorySecrets : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _kept = new(StringComparer.Ordinal);
    private int _failingWrites;

    public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_kept.GetValueOrDefault(SecretNames.Required(name)));

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _failingWrites, 0) == 1)
        {
            throw new HttpRequestException("The vault did not answer.");
        }

        _kept[SecretNames.Required(name)] = value;

        return Task.CompletedTask;
    }

    public Task ForgetAsync(string name, CancellationToken cancellationToken)
    {
        _kept.TryRemove(SecretNames.Required(name), out _);

        return Task.CompletedTask;
    }

    // A vault losing what a connection still names: a key deleted, or a database moved to a deployment whose
    // secrets live somewhere else. The row says connected and the money cannot move (D-190).
    public void Clear() => _kept.Clear();

    // A vault that is down for one write.
    public void FailNextWrite() => Interlocked.Exchange(ref _failingWrites, 1);
}
