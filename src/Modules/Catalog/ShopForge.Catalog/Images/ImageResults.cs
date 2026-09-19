using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ShopForge.Shared.Files;

namespace ShopForge.Catalog.Images;

internal static class ImageResults
{
    public static async Task<Results<FileStreamHttpResult, NotFound>> StreamAsync(
        string? filePath,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var file = filePath is null ? null : await fileStorage.OpenReadAsync(filePath, cancellationToken);

        return file is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(file.Content, file.ContentType);
    }
}
