using Microsoft.Extensions.Diagnostics.HealthChecks;
using ShopForge.Shared.Security;

namespace ShopForge.Api.Health;

// A provider whose credentials cannot be read cannot take money, and finding that out when a shopper is waiting
// is the wrong moment. An instance that cannot reach the secret store leaves rotation rather than serving
// checkouts it will fail (D-139).
internal sealed class SecretStoreHealthCheck(ISecretStore secrets) : IHealthCheck
{
    // A name nothing uses: what is being asked is whether the store answers, not what it holds.
    private const string Probe = "readiness-probe";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await secrets.FindAsync(Probe, cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The secret store is not reachable.", exception);
        }
    }
}
