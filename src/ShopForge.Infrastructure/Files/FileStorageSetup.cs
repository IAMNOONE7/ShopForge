using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace ShopForge.Infrastructure.Files;

public static class FileStorageSetup
{
    public static async Task CreateFileStorageContainerAsync(this IServiceProvider services, CancellationToken cancellationToken = default) =>
        await services.GetRequiredService<BlobContainerClient>().CreateIfNotExistsAsync(cancellationToken: cancellationToken);
}
