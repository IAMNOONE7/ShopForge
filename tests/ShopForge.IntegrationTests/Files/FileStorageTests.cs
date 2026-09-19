using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Files;

namespace ShopForge.IntegrationTests.Files;

public sealed class FileStorageTests(ShopForgeApiFactory factory)
{
    private IFileStorage Storage => factory.Services.GetRequiredService<IFileStorage>();

    [Fact]
    public async Task Saved_file_can_be_read_back_with_its_content_type()
    {
        var path = $"tests/{Guid.NewGuid():N}.txt";

        await Storage.SaveAsync(path, new MemoryStream("hello"u8.ToArray()), "text/plain", TestContext.Current.CancellationToken);
        var file = await Storage.OpenReadAsync(path, TestContext.Current.CancellationToken);

        Assert.NotNull(file);
        Assert.Equal("text/plain", file.ContentType);
        using var reader = new StreamReader(file.Content, Encoding.UTF8);
        Assert.Equal("hello", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_or_deleted_file_reads_as_null()
    {
        var path = $"tests/{Guid.NewGuid():N}.txt";
        await Storage.SaveAsync(path, new MemoryStream([1, 2, 3]), "application/octet-stream", TestContext.Current.CancellationToken);

        await Storage.DeleteAsync(path, TestContext.Current.CancellationToken);

        Assert.Null(await Storage.OpenReadAsync(path, TestContext.Current.CancellationToken));
        Assert.Null(await Storage.OpenReadAsync($"tests/{Guid.NewGuid():N}", TestContext.Current.CancellationToken));
    }
}
