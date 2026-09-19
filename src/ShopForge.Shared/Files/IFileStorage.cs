namespace ShopForge.Shared.Files;

public interface IFileStorage
{
    Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken);

    Task<StoredFile?> OpenReadAsync(string path, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);
}

public sealed record StoredFile(Stream Content, string ContentType);
