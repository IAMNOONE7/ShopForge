using Microsoft.Extensions.Configuration;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Secrets;

// What a deployment with no vault uses: development, CI, and a single merchant running their own installation
// from environment variables (D-138's transitional fallback). It can read what configuration was given and it
// cannot write, because configuration is not somewhere an application puts things at runtime — and saying so is
// better than appearing to save a secret that would vanish on the next restart.
internal sealed class ConfigurationSecretStore(IConfiguration configuration) : ISecretStore
{
    public const string Section = "ProviderSecrets";

    public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(configuration[$"{Section}:{SecretNames.Required(name)}"]);

    public Task SetAsync(string name, string value, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This deployment has no secret store, so a provider's credentials are set in configuration rather than saved here.");

    public Task ForgetAsync(string name, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This deployment has no secret store, so a provider's credentials are removed from configuration rather than here.");
}
