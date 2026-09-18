using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;

namespace ShopForge.Api.Health;

internal static class HealthEndpoints
{
    public const string ReadinessTag = "ready";

    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no checks: a database outage should take the instance out of rotation
        // through readiness, not get it restarted.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .WithHttpLogging(HttpLoggingFields.None);

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) })
            .WithHttpLogging(HttpLoggingFields.None);
    }
}
