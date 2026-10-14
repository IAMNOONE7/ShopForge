using System.Collections.Concurrent;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using ShopForge.Infrastructure.Secrets;

namespace ShopForge.UnitTests.Security;

// What rotating a merchant's credential actually does, which the rotation policy has to be able to state. A
// lookup is a network call on the path a shopper waits on, so a value is held for a few minutes; a write drops
// the held copy on the replica that made it, and every other replica keeps its own until it expires (D-139).
// These pin both halves, so the window the policy names is the window the code has.
public sealed class KeyVaultSecretStoreTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_value_is_held_rather_than_asked_for_on_every_payment()
    {
        var vault = new FakeVault();
        await vault.SetSecretAsync("store-a-comgate", "first", CancellationToken);
        var store = new KeyVaultSecretStore(vault, new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));

        // Changed behind its back — rotated in the portal, say — and the held copy is what it answers with.
        await vault.SetSecretAsync("store-a-comgate", "second", CancellationToken);

        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));
        Assert.Equal(1, vault.Reads);
    }

    // Rotation from the admin is immediate where it was made.
    [Fact]
    public async Task Writing_a_value_drops_the_held_copy_on_the_replica_that_wrote_it()
    {
        var vault = new FakeVault();
        var store = new KeyVaultSecretStore(vault, new MemoryCache(new MemoryCacheOptions()));
        await store.SetAsync("store-a-comgate", "first", CancellationToken);
        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));

        await store.SetAsync("store-a-comgate", "second", CancellationToken);

        Assert.Equal("second", await store.FindAsync("store-a-comgate", CancellationToken));
    }

    [Fact]
    public async Task Forgetting_a_value_drops_the_held_copy_on_the_replica_that_forgot_it()
    {
        var vault = new FakeVault();
        var store = new KeyVaultSecretStore(vault, new MemoryCache(new MemoryCacheOptions()));
        await store.SetAsync("store-a-comgate", "first", CancellationToken);
        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));

        await store.ForgetAsync("store-a-comgate", CancellationToken);

        Assert.Null(await store.FindAsync("store-a-comgate", CancellationToken));
    }

    // The window the rotation policy names, shown rather than asserted in prose: two replicas, two caches, one
    // vault. The replica that did not make the change keeps answering with the old value until its copy expires.
    // This is why revoking a compromised credential means revoking it at the provider — then the old value stops
    // working everywhere at once, whatever any replica is holding (D-191).
    [Fact]
    public async Task Another_replica_keeps_the_old_value_until_its_copy_expires()
    {
        var vault = new FakeVault();
        var writer = new KeyVaultSecretStore(vault, new MemoryCache(new MemoryCacheOptions()));
        var other = new KeyVaultSecretStore(vault, new MemoryCache(new MemoryCacheOptions()));
        await writer.SetAsync("store-a-comgate", "first", CancellationToken);
        Assert.Equal("first", await other.FindAsync("store-a-comgate", CancellationToken));

        await writer.SetAsync("store-a-comgate", "second", CancellationToken);

        Assert.Equal("second", await writer.FindAsync("store-a-comgate", CancellationToken));
        Assert.Equal("first", await other.FindAsync("store-a-comgate", CancellationToken));
    }

    // A reference to nothing is not an error: the provider is simply not offered, rather than failing when a
    // shopper tries to pay.
    [Fact]
    public async Task A_name_pointing_at_nothing_answers_nothing()
    {
        var store = new KeyVaultSecretStore(new FakeVault(), new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await store.FindAsync("store-a-comgate", CancellationToken));
    }

    // The Azure client is built to be subclassed for this: its constructor and its methods are there to be
    // overridden. Everything the store calls is answered from a dictionary.
    private sealed class FakeVault : SecretClient
    {
        private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public int Reads { get; private set; }

        // The overload the store's call binds to since 4.11, which added the content type; the older one is kept for
        // whatever still calls it.
        public override Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version = null, SecretContentType? outContentType = null, CancellationToken cancellationToken = default) =>
            GetSecretAsync(name, version, cancellationToken);

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Reads++;

            return _secrets.TryGetValue(name, out var value)
                ? Task.FromResult(Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value), new NoResponse()))
                : throw new RequestFailedException(404, "Not found");
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            _secrets[name] = value;

            return Task.FromResult(Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value), new NoResponse()));
        }

        public override Task<DeleteSecretOperation> StartDeleteSecretAsync(string name, CancellationToken cancellationToken = default)
        {
            // Gone already, which is the answer the store treats as success: the operation a real delete returns
            // has no public way to be built, and the store never reads it.
            _secrets.TryRemove(name, out _);

            throw new RequestFailedException(404, "Not found");
        }
    }

    private sealed class NoResponse : Response
    {
        public override int Status => 200;

        public override string ReasonPhrase => "OK";

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<Azure.Core.HttpHeader> EnumerateHeaders() => [];

        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;

            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = [];

            return false;
        }
    }
}
