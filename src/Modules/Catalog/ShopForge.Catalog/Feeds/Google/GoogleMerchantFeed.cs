using System.Globalization;
using System.Xml;
using ShopForge.Shared.Feeds;

namespace ShopForge.Catalog.Feeds.Google;

// Everything that depends on reading Google's documentation correctly, in one file: the element names, the
// namespace and the vocabulary of its values. Everything around it is ours and is tested (D-155's habit).
// Nothing has ever been submitted to Merchant Center, so this is the file to check against the current
// specification before the first submission.
internal sealed class GoogleMerchantFeed : IProductFeedFormat
{
    public const string FeedKey = "google";

    private const string Namespace = "http://base.google.com/ns/1.0";

    public string Key => FeedKey;

    public string ContentType => "application/xml";

    public async Task WriteAsync(
        Stream destination,
        FeedStore store,
        IAsyncEnumerable<FeedProduct> products,
        CancellationToken cancellationToken)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        await using var writer = XmlWriter.Create(destination, settings);

        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(prefix: null, "rss", null);
        await writer.WriteAttributeStringAsync("xmlns", "g", null, Namespace);
        await writer.WriteAttributeStringAsync(prefix: null, "version", null, "2.0");
        await writer.WriteStartElementAsync(prefix: null, "channel", null);
        await writer.WriteElementStringAsync(prefix: null, "title", null, store.Name);
        await writer.WriteElementStringAsync(prefix: null, "link", null, store.HomeUrl);
        await writer.WriteElementStringAsync(prefix: null, "description", null, $"{store.Name} product feed");

        await foreach (var product in products.WithCancellation(cancellationToken))
        {
            await WriteItemAsync(writer, store, product);
        }

        await writer.WriteEndElementAsync();
        await writer.WriteEndElementAsync();
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
    }

    private static async Task WriteItemAsync(XmlWriter writer, FeedStore store, FeedProduct product)
    {
        await writer.WriteStartElementAsync(prefix: null, "item", null);

        await Google(writer, "id", product.Sku);
        await writer.WriteElementStringAsync(prefix: null, "title", null, product.Name);
        await writer.WriteElementStringAsync(prefix: null, "description", null, product.Description ?? product.Name);
        await writer.WriteElementStringAsync(prefix: null, "link", null, product.Url);

        if (product.ImageUrl is { } image)
        {
            await Google(writer, "image_link", image);
        }

        foreach (var more in product.MoreImageUrls.Take(10))
        {
            await Google(writer, "additional_image_link", more);
        }

        // Google's own words, not ours: a shop with none left is "out_of_stock" whatever we call it inside.
        await Google(writer, "availability", product.Available > 0 ? "in_stock" : "out_of_stock");

        // Prices are stored with VAT included (D-044), which is what Google expects where the displayed price
        // includes tax.
        await Google(writer, "price", $"{product.Price.ToString("0.00", CultureInfo.InvariantCulture)} {product.Currency}");

        if (product.Brand is { } brand)
        {
            await Google(writer, "brand", brand);
        }

        if (product.Gtin is { } gtin)
        {
            await Google(writer, "gtin", gtin);
        }

        if (product.PartNumber is { } partNumber)
        {
            await Google(writer, "mpn", partNumber);
        }

        // Nobody has said, so the thing is new: unlike the condition column, where silence had to stay silence
        // because a shop selling second-hand goods must not have them declared new (D-163), a feed demands a
        // value and Google's default for an omitted condition is "new" anyway. Saying it plainly is clearer
        // than letting it be assumed.
        await Google(writer, "condition", (product.Condition ?? "New").ToLowerInvariant());

        // A thing with neither a barcode nor a part number cannot be matched to anybody else's listing, and
        // Google wants telling that rather than guessing.
        if (product.Gtin is null && product.PartNumber is null)
        {
            await Google(writer, "identifier_exists", "no");
        }

        // One product sold in several forms is one thing in several sizes, not several things.
        if (product.HasSiblings)
        {
            await Google(writer, "item_group_id", product.GroupId);
        }

        foreach (var rate in store.Shipping)
        {
            await writer.WriteStartElementAsync("g", "shipping", Namespace);
            await Google(writer, "country", rate.Country);
            await Google(writer, "service", rate.Service);
            await Google(writer, "price", $"{rate.Price.ToString("0.00", CultureInfo.InvariantCulture)} {store.Currency}");
            await writer.WriteEndElementAsync();
        }

        await writer.WriteEndElementAsync();
    }

    private static Task Google(XmlWriter writer, string name, string value) =>
        writer.WriteElementStringAsync("g", name, Namespace, value);
}
