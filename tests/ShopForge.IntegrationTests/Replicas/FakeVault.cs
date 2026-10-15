using System.Collections.Concurrent;
using Azure;
using Azure.Security.KeyVault.Secrets;

namespace ShopForge.IntegrationTests.Replicas;

// One vault for several replicas, as a deployment has. The Azure client is built to be subclassed for this: its
// constructor and its methods are there to be overridden, and everything the store calls is answered from a dictionary.
internal sealed class FakeVault : SecretClient
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    // The overload the store's call binds to since 4.11, which added the content type.
    public override Task<Response<KeyVaultSecret>> GetSecretAsync(
        string name, string? version = null, SecretContentType? outContentType = null, CancellationToken cancellationToken = default) =>
        GetSecretAsync(name, version, cancellationToken);

    public override Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default) =>
        _secrets.TryGetValue(name, out var value)
            ? Task.FromResult(Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value), new NoResponse()))
            : throw new RequestFailedException(404, "Not found");

    public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        _secrets[name] = value;

        return Task.FromResult(Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value), new NoResponse()));
    }

    // Gone already is what the store treats as success: the operation a real delete returns has no public way to be
    // built, and the store never reads it.
    public override Task<DeleteSecretOperation> StartDeleteSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(name, out _);

        throw new RequestFailedException(404, "Not found");
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
