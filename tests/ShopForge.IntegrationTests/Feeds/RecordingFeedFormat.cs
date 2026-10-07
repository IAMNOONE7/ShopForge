using System.Text;
using ShopForge.Shared.Feeds;

namespace ShopForge.IntegrationTests.Feeds;

// Stands in for a real shopping engine's format. It records what the engine handed it, so a test can read
// exactly what would have gone into the document without depending on anybody's XML (D-168).
internal sealed class RecordingFeedFormat(string key = "stub") : IProductFeedFormat
{
    public string Key => key;

    public string ContentType => "application/xml";

    public FeedStore? LastStore { get; private set; }

    public List<FeedProduct> LastProducts { get; } = [];

    public bool Fails { get; set; }

    public async Task WriteAsync(
        Stream destination,
        FeedStore store,
        IAsyncEnumerable<FeedProduct> products,
        CancellationToken cancellationToken)
    {
        LastStore = store;
        LastProducts.Clear();

        await using var writer = new StreamWriter(destination, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync($"<feed store=\"{store.Name}\" language=\"{store.Language}\" currency=\"{store.Currency}\">");

        await foreach (var product in products.WithCancellation(cancellationToken))
        {
            if (Fails)
            {
                throw new InvalidOperationException("The format could not write this product.");
            }

            LastProducts.Add(product);
            await writer.WriteLineAsync($"  <item sku=\"{product.Sku}\" url=\"{product.Url}\" available=\"{product.Available}\" />");
        }

        await writer.WriteLineAsync("</feed>");
    }
}
