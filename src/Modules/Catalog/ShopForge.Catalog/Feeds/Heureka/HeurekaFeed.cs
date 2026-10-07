using System.Globalization;
using System.Xml;
using ShopForge.Shared.Feeds;

namespace ShopForge.Catalog.Feeds.Heureka;

// Heureka's SHOPITEM document. As with Google's, every element name and every value's vocabulary is in this one
// file, because nothing has ever been submitted to Heureka either (D-170).
internal sealed class HeurekaFeed : IProductFeedFormat
{
    public const string FeedKey = "heureka";

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
            await WriteItemAsync(writer, store, product);
        }

        await writer.WriteEndElementAsync();
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
    }

    private static async Task WriteItemAsync(XmlWriter writer, FeedStore store, FeedProduct product)
    {
        await writer.WriteStartElementAsync(prefix: null, "SHOPITEM", null);

        await Element(writer, "ITEM_ID", product.Sku);

        // PRODUCTNAME is what a shopper reads; PRODUCT is what Heureka matches on, and for a shop without a
        // separate matching name they are the same words.
        await Element(writer, "PRODUCTNAME", product.Name);
        await Element(writer, "PRODUCT", product.Name);

        if (product.Description is { } description)
        {
            await Element(writer, "DESCRIPTION", description);
        }

        await Element(writer, "URL", product.Url);

        if (product.ImageUrl is { } image)
        {
            await Element(writer, "IMGURL", image);
        }

        foreach (var more in product.MoreImageUrls)
        {
            await Element(writer, "IMGURL_ALTERNATIVE", more);
        }

        // Prices are stored with VAT included (D-044), which is what this element means.
        await Element(writer, "PRICE_VAT", product.Price.ToString("0.00", CultureInfo.InvariantCulture));

        if (product.Brand is { } brand)
        {
            await Element(writer, "MANUFACTURER", brand);
        }

        // Only when the merchant has mapped it. Publishing the shop's own taxonomy into Heureka's field is
        // what the mapping exists to avoid, so an unmapped category says nothing and is reported instead.
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

        // Days until dispatch. A thing in stock goes today; for one that is not, the shop does not know when,
        // and Heureka reads an absent date as unknown rather than as never.
        if (product.Available > 0)
        {
            await Element(writer, "DELIVERY_DATE", "0");
        }

        foreach (var rate in store.Shipping)
        {
            await writer.WriteStartElementAsync(prefix: null, "DELIVERY", null);
            await Element(writer, "DELIVERY_ID", rate.Service);
            await Element(writer, "DELIVERY_PRICE", rate.Price.ToString("0.00", CultureInfo.InvariantCulture));
            await writer.WriteEndElementAsync();
        }

        foreach (var parameter in product.Parameters)
        {
            await writer.WriteStartElementAsync(prefix: null, "PARAM", null);
            await Element(writer, "PARAM_NAME", parameter.Name);
            await Element(writer, "VAL", parameter.Value);

            if (parameter.Unit is { } unit)
            {
                await Element(writer, "UNIT", unit);
            }

            await writer.WriteEndElementAsync();
        }

        if (product.HasSiblings)
        {
            await Element(writer, "ITEMGROUP_ID", product.GroupId);
        }

        await writer.WriteEndElementAsync();
    }

    private static Task Element(XmlWriter writer, string name, string value) =>
        writer.WriteElementStringAsync(prefix: null, name, null, value);
}
