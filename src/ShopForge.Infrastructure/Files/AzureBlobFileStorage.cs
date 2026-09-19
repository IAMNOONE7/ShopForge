using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ShopForge.Shared.Files;

namespace ShopForge.Infrastructure.Files;

internal sealed class AzureBlobFileStorage(BlobContainerClient container) : IFileStorage
{
    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };

        await container.GetBlobClient(path).UploadAsync(content, options, cancellationToken);
    }

    public async Task<StoredFile?> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var download = await container.GetBlobClient(path).DownloadStreamingAsync(cancellationToken: cancellationToken);

            return new StoredFile(download.Value.Content, download.Value.Details.ContentType);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken) =>
        await container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
