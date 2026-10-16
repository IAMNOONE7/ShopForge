using Microsoft.Extensions.Diagnostics.HealthChecks;
using ShopForge.Infrastructure.Persistence;

namespace ShopForge.Api.Health;

// A revision whose migrations have not been applied is not ready, so the platform keeps the previous one serving
// until they are (D-205).
internal sealed class SchemaHealthCheck(SchemaGate schema) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = await schema.PendingAsync(cancellationToken);

            return pending.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"The database is {pending.Count} migration(s) behind this build, starting with {pending[0]}.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The database could not say which migrations it has.", exception);
        }
    }
}
