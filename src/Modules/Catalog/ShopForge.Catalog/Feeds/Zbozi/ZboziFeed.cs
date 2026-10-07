using System.Globalization;
using System.Xml;
using ShopForge.Shared.Feeds;

namespace ShopForge.Catalog.Feeds.Zbozi;

// Seznam's shopping engine. Its document is close enough to Heureka's to look like a candidate for sharing and
// is deliberately not shared: the two drift on their own timetables, and two short composers reading one
// gathering is the cheaper mistake than one composer with two sets of exceptions in it (D-171).
//
// As with the other two, every element name here comes from the brief rather than from reading Zboží's current
// specification, and nothing has ever been submitted to them. This is the file to check first.
internal sealed class ZboziFeed : IProductFeedFormat
{
    public const string FeedKey = "zbozi";

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
        await writer.WriteStartElementAsync(prefix: null, "SHOP", null);

        await foreach (var product in products.WithCancellation(cancellationToken))
        {
            await WriteItemAsync(writer, product);
        }

        await writer.WriteEndElementAsync();
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
    }

    private static async Task WriteItemAsync(XmlWriter writer, FeedProduct product)
    {
        await writer.WriteStartElementAsync(prefix: null, "SHOPITEM", null);

        await Element(writer, "ITEM_ID", product.Sku);
        await Element(writer, "PRODUCTNAME", product.Name);

        if (product.Description is { } description)
        {
            await Element(writer, "DESCRIPTION", description);
        }

        await Element(writer, "URL", product.Url);

        if (product.ImageUrl is { } image)
        {
            await Element(writer, "IMGURL", image);
        }

        // Prices are stored with VAT included (D-044), which is what this element means.
        await Element(writer, "PRICE_VAT", product.Price.ToString("0.00", CultureInfo.InvariantCulture));

        // Days until dispatch: nothing for a thing the shop cannot promise a date for.
        if (product.Available > 0)
        {
            await Element(writer, "DELIVERY_DATE", "0");
        }

        // Zboží's own taxonomy, and only when the merchant has said what this category is called there. The
        // mapping is keyed by engine, so what was said for Heureka is not said here (D-147, D-170).
        if (product.EngineCategory is { } category)
        {
            await Element(writer, "CATEGORYTEXT", category);
        }

        if (product.Gtin is { } gtin)
        {
            await Element(writer, "EAN", gtin);
        }

        if (product.PartNumber is { } partNumber)
        {
            await Element(writer, "PRODUCTNO", partNumber);
        }

        if (product.Brand is { } brand)
        {
            await Element(writer, "MANUFACTURER", brand);
        }

        await writer.WriteEndElementAsync();
    }

    private static Task Element(XmlWriter writer, string name, string value) =>
        writer.WriteElementStringAsync(prefix: null, name, null, value);
}
