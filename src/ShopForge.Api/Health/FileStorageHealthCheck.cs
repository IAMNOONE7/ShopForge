using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ShopForge.Api.Health;

// Product images and logos come from blob storage; an instance that cannot reach it should not serve a storefront.
internal sealed class FileStorageHealthCheck(BlobContainerClient container) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await container.ExistsAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Blob storage is not reachable.", exception);
        }
    }
}
