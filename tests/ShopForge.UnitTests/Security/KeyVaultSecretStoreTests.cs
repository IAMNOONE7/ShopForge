using System.Collections.Concurrent;
using Azure;
using Azure.Security.KeyVault.Secrets;
using ShopForge.Infrastructure.Secrets;
using ShopForge.Shared.Caching;

namespace ShopForge.UnitTests.Security;

// What rotating a merchant's credential actually does, which the rotation policy has to be able to state. A
// lookup is a network call on the path a shopper waits on, so a value is held for a few minutes. A write or a
// removal drops the held copy on every replica at once, and a replica that cannot hear the others holds nothing
// (D-202). The replicas here hear each other instantly; whether the database's channel carries the word is the
// integration tests' business.
public sealed class KeyVaultSecretStoreTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_value_is_held_rather_than_asked_for_on_every_payment()
    {
        var vault = new FakeVault();
        await vault.SetSecretAsync("store-a-comgate", "first", CancellationToken);
        var store = new KeyVaultSecretStore(vault, new Replicas().Join());

        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));

        // Changed behind its back — rotated in the portal, say — and the held copy is what it answers with.
        await vault.SetSecretAsync("store-a-comgate", "second", CancellationToken);

        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));
        Assert.Equal(1, vault.Reads);
    }

    [Fact]
    public async Task Writing_a_value_drops_the_held_copy_on_the_replica_that_wrote_it()
    {
        var vault = new FakeVault();
        var store = new KeyVaultSecretStore(vault, new Replicas().Join());
        await store.SetAsync("store-a-comgate", "first", CancellationToken);
        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));

        await store.SetAsync("store-a-comgate", "second", CancellationToken);

        Assert.Equal("second", await store.FindAsync("store-a-comgate", CancellationToken));
    }

    [Fact]
    public async Task Forgetting_a_value_drops_the_held_copy_on_the_replica_that_forgot_it()
    {
        var vault = new FakeVault();
        var store = new KeyVaultSecretStore(vault, new Replicas().Join());
        await store.SetAsync("store-a-comgate", "first", CancellationToken);
        Assert.Equal("first", await store.FindAsync("store-a-comgate", CancellationToken));

        await store.ForgetAsync("store-a-comgate", CancellationToken);

        Assert.Null(await store.FindAsync("store-a-comgate", CancellationToken));
    }

    // Rotation: the replica that did not make the change stops using the old value as soon as it is told, rather
    // than when its copy expires — which is the window the rotation policy used to have to name (D-191).
    [Fact]
    public async Task Another_replica_drops_its_copy_when_the_value_is_rotated()
    {
        var vault = new FakeVault();
        var replicas = new Replicas();
        var writer = new KeyVaultSecretStore(vault, replicas.Join());
        var other = new KeyVaultSecretStore(vault, replicas.Join());
        await writer.SetAsync("store-a-comgate", "first", CancellationToken);
        Assert.Equal("first", await other.FindAsync("store-a-comgate", CancellationToken));

        await writer.SetAsync("store-a-comgate", "second", CancellationToken);

        Assert.Equal("second", await other.FindAsync("store-a-comgate", CancellationToken));
    }

    // Revocation, the case a held copy matters most for.
    [Fact]
    public async Task Another_replica_drops_its_copy_when_the_value_is_removed()
    {
        var vault = new FakeVault();
        var replicas = new Replicas();
        var writer = new KeyVaultSecretStore(vault, replicas.Join());
        var other = new KeyVaultSecretStore(vault, replicas.Join());
        await writer.SetAsync("store-a-comgate", "first", CancellationToken);
        Assert.Equal("first", await other.FindAsync("store-a-comgate", CancellationToken));

        await writer.ForgetAsync("store-a-comgate", CancellationToken);

        Assert.Null(await other.FindAsync("store-a-comgate", CancellationToken));
    }

    // "Nothing there" is held too, so a merchant who connects a gateway would otherwise find the next replica still
    // saying there is nothing for five minutes.
    [Fact]
    public async Task A_value_saved_on_one_replica_is_found_on_another_that_had_found_nothing()
    {
        var vault = new FakeVault();
        var replicas = new Replicas();
        var writer = new KeyVaultSecretStore(vault, replicas.Join());
        var other = new KeyVaultSecretStore(vault, replicas.Join());
        Assert.Null(await other.FindAsync("store-a-comgate", CancellationToken));

        await writer.SetAsync("store-a-comgate", "first", CancellationToken);

        Assert.Equal("first", await other.FindAsync("store-a-comgate", CancellationToken));
    }

    // A replica that cannot hear the others would not hear a revocation either, so it keeps nothing to be wrong about.
    [Fact]
    public async Task A_replica_that_cannot_hear_the_others_asks_the_vault_every_time()
    {
        var vault = new FakeVault();
        await vault.SetSecretAsync("store-a-comgate", "first", CancellationToken);
        var store = new KeyVaultSecretStore(vault, new Replicas().Join(hearing: false));

        await store.FindAsync("store-a-comgate", CancellationToken);
        await store.FindAsync("store-a-comgate", CancellationToken);

        Assert.Equal(2, vault.Reads);
    }

    // A reference to nothing is not an error: the provider is simply not offered, rather than failing when a
    // shopper tries to pay.
    [Fact]
    public async Task A_name_pointing_at_nothing_answers_nothing()
    {
        var store = new KeyVaultSecretStore(new FakeVault(), new Replicas().Join());

        Assert.Null(await store.FindAsync("store-a-comgate", CancellationToken));
    }

    // Replicas that hear each other at once: what one forgets, every one forgets.
    private sealed class Replicas
    {
        private readonly List<Replica> _all = [];

        public IReplicaCache Join(bool hearing = true)
        {
            var replica = new Replica(this, hearing);
            _all.Add(replica);

            return replica;
        }

        private sealed class Replica(Replicas replicas, bool hearing) : IReplicaCache
        {
            private readonly ConcurrentDictionary<string, object?> _held = new(StringComparer.Ordinal);

            public async Task<T> GetAsync<T>(
                string key, TimeSpan heldFor, Func<CancellationToken, Task<T>> load, Func<T, bool> keep, CancellationToken cancellationToken)
            {
                if (hearing && _held.TryGetValue(key, out var held))
                {
                    return (T)held!;
                }

                var loaded = await load(cancellationToken);

                if (hearing && keep(loaded))
                {
                    _held[key] = loaded;
                }

                return loaded;
            }

            public Task ForgetAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
            {
                foreach (var replica in replicas._all)
                {
                    foreach (var key in keys)
                    {
                        replica._held.TryRemove(key, out _);
                    }
                }

                return Task.CompletedTask;
            }
        }
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
