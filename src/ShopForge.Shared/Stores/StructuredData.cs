using System.Globalization;
using System.Text.Json.Serialization;

namespace ShopForge.Shared.Stores;

// What a page declares itself to be, in the vocabulary Google reads, composed from facts the page already has
// rather than written by hand into a template (D-173).
//
// Everything here is schema.org's spelling, which is why the names are not this codebase's: `gtin13`, `mpn`,
// `itemCondition`. One file to check against Google's documentation, on the same terms as a feed format
// (D-169) — nothing composed here has ever been submitted to the Rich Results test.
public static class StructuredData
{
    private const string Vocabulary = "https://schema.org";

    // A thing for sale, as its own page declares it.
    public static LinkedProduct Product(StoreAddress address, ProductFacts product) => new()
    {
        Name = product.Name,
        Description = product.Description,
        Sku = product.Sku,
        Gtin13 = product.Gtin,
        Mpn = product.PartNumber,
        Url = address.Product(product.Slug),
        // A thing with no picture says nothing rather than declaring an empty list of them. Google asks for an
        // image on a product, so this is a gap a merchant has to fill, not one to paper over.
        Image = product.ImageUrls.Count == 0 ? null : [.. product.ImageUrls],
        Brand = product.Brand is null ? null : new LinkedBrand { Name = product.Brand },
        Offers = Offers(address, product),

        // A shop with no reviews says nothing about its rating rather than declaring it nought out of five,
        // which is what a zero would mean to anybody reading it (D-089).
        AggregateRating = product.ReviewCount == 0
            ? null
            : new LinkedRating { RatingValue = Number(product.Rating), ReviewCount = product.ReviewCount },
    };

    // A trail from the shop's front page down to where the reader is, which is what a breadcrumb is for and
    // what the category tree answers (D-172). Positions are one-based, as schema.org asks.
    public static LinkedBreadcrumbs Breadcrumbs(StoreAddress address, string homeName, IReadOnlyList<Crumb> trail)
    {
        var steps = new List<Crumb> { new(homeName, address.Home) };
        steps.AddRange(trail);

        return new LinkedBreadcrumbs
        {
            ItemListElement =
            [
                .. steps.Select((step, index) => new LinkedCrumb
                {
                    Position = index + 1,
                    Name = step.Name,

                    // The last crumb is where the reader already is, and schema.org asks for no link on it.
                    Item = index == steps.Count - 1 ? null : step.Url,
                }),
            ],
        };
    }

    // Who is selling, which is the legal identity an invoice already names (D-078).
    public static LinkedOrganization Organization(StoreAddress address, string name, SellerDetails? seller, string? logoUrl) => new()
    {
        Name = name,
        Url = address.Home,
        Logo = logoUrl,
        LegalName = seller?.LegalName,
        VatId = seller?.VatNumber,
        TaxId = seller?.RegistrationNumber,
        Address = seller is null
            ? null
            : new LinkedAddress
            {
                StreetAddress = seller.Line1,
                AddressLocality = seller.City,
                PostalCode = seller.PostalCode,
                AddressCountry = seller.Country,
            },
    };

    public static LinkedWebSite WebSite(StoreAddress address, string name) => new()
    {
        Name = name,
        Url = address.Home,
        InLanguage = address.Language,
    };

    // One offer for a thing sold one way; an aggregate for a thing sold in several forms. Prices do not vary
    // by form in ShopForge today, so the low and the high are the same number — the shape is what changes if
    // they ever do, and the count is the part that is worth saying either way.
    private static object Offers(StoreAddress address, ProductFacts product)
    {
        var availability = $"{Vocabulary}/{(product.Available > 0 ? "InStock" : "OutOfStock")}";
        var price = Number(product.Price);

        if (product.FormCount > 1)
        {
            return new LinkedAggregateOffer
            {
                LowPrice = price,
                HighPrice = price,
                PriceCurrency = product.Currency,
                OfferCount = product.FormCount,
                Availability = availability,
                Url = address.Product(product.Slug),
            };
        }

        return new LinkedOffer
        {
            Price = price,
            PriceCurrency = product.Currency,
            Availability = availability,

            // Condition is said only where somebody has said it: a shop selling second-hand goods must not
            // have them declared new by a default nobody chose (D-163). Google assumes new when it is
            // missing, which is the same thing it assumes from a feed.
            ItemCondition = product.Condition is null ? null : $"{Vocabulary}/{product.Condition}Condition",
            Url = address.Product(product.Slug),
        };
    }

    // Prices and ratings go out as strings, because that is what schema.org's examples use and a decimal
    // rendered by the current culture would put a comma in a price for half of Europe.
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

// What the composer needs to know about a thing for sale. A record rather than a pile of arguments, because
// the next field somebody adds should not change every caller.
public sealed record ProductFacts(
    string Name,
    string? Description,
    string Slug,
    string Sku,
    decimal Price,
    string Currency,
    int Available,
    int FormCount,
    IReadOnlyList<string> ImageUrls,
    string? Brand,
    string? Gtin,
    string? PartNumber,
    string? Condition,
    decimal Rating,
    int ReviewCount);

public sealed record Crumb(string Name, string Url);

// The documents themselves. `@context` is written on each one because each is published on its own, and a
// null is left out rather than declared empty: an absent field means "not stated", while a present one means
// the shop has said so.
public abstract record LinkedDocument([property: JsonIgnore] string Type)
{
    [JsonPropertyName("@context")]
    public string Context => "https://schema.org";

    // The parameter above is ignored on purpose: it is how a document names itself to this code, while this is
    // the only name that goes out. Leaving both in would publish a second, lower-case "type" beside it.
    [JsonPropertyName("@type")]
    public string TypeName => Type;
}

public sealed record LinkedProduct() : LinkedDocument("Product")
{
    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    public required string Sku { get; init; }

    [JsonPropertyName("gtin13")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Gtin13 { get; init; }

    [JsonPropertyName("mpn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mpn { get; init; }

    public required string Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string>? Image { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LinkedBrand? Brand { get; init; }

    public required object Offers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LinkedRating? AggregateRating { get; init; }
}

// The nested shapes carry a type and no context: they are part of the document above them, not published on
// their own.
public sealed record LinkedBrand
{
    [JsonPropertyName("@type")]
    public string Type => "Brand";

    public required string Name { get; init; }
}

public sealed record LinkedOffer
{
    [JsonPropertyName("@type")]
    public string Type => "Offer";

    public required string Price { get; init; }

    public required string PriceCurrency { get; init; }

    public required string Availability { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemCondition { get; init; }

    public required string Url { get; init; }
}

public sealed record LinkedAggregateOffer
{
    [JsonPropertyName("@type")]
    public string Type => "AggregateOffer";

    public required string LowPrice { get; init; }

    public required string HighPrice { get; init; }

    public required string PriceCurrency { get; init; }

    public required int OfferCount { get; init; }

    public required string Availability { get; init; }

    public required string Url { get; init; }
}

public sealed record LinkedRating
{
    [JsonPropertyName("@type")]
    public string Type => "AggregateRating";

    public required string RatingValue { get; init; }

    public required int ReviewCount { get; init; }
}

public sealed record LinkedBreadcrumbs() : LinkedDocument("BreadcrumbList")
{
    public IReadOnlyList<LinkedCrumb> ItemListElement { get; init; } = [];
}

public sealed record LinkedCrumb
{
    [JsonPropertyName("@type")]
    public string Type => "ListItem";

    public required int Position { get; init; }

    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Item { get; init; }
}

public sealed record LinkedOrganization() : LinkedDocument("Organization")
{
    public required string Name { get; init; }

    public required string Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Logo { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegalName { get; init; }

    [JsonPropertyName("vatID")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VatId { get; init; }

    [JsonPropertyName("taxID")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaxId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LinkedAddress? Address { get; init; }
}

public sealed record LinkedAddress
{
    [JsonPropertyName("@type")]
    public string Type => "PostalAddress";

    public required string StreetAddress { get; init; }

    public required string AddressLocality { get; init; }

    public required string PostalCode { get; init; }

    public required string AddressCountry { get; init; }
}

public sealed record LinkedWebSite() : LinkedDocument("WebSite")
{
    public required string Name { get; init; }

    public required string Url { get; init; }

    public required string InLanguage { get; init; }
}
