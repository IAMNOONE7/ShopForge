using System.Text.Json;
using System.Text.Json.Nodes;
using ShopForge.Shared.Stores;

namespace ShopForge.UnitTests.Seo;

// The composed documents, read as they go over the wire rather than as objects: the whole point of this is the
// JSON a crawler sees, including which fields are absent (D-173).
public sealed class StructuredDataTests
{
    private static readonly StoreAddress Address = new("acme.example", "cs");

    private static readonly JsonSerializerOptions AsTheApiWould = new(JsonSerializerDefaults.Web);

    private static ProductFacts Chair(
        int available = 5,
        int formCount = 1,
        string? gtin = "8594000000006",
        string? condition = null,
        decimal rating = 0m,
        int reviewCount = 0) =>
        new(
            "Oak Chair",
            "A chair of oak.",
            "oak-chair",
            "FURN-OAK-CHAIR",
            1299.00m,
            "CZK",
            available,
            formCount,
            ["https://acme.example/api/storefront/products/1/images/2"],
            "Thonet",
            gtin,
            "214-OAK",
            condition,
            rating,
            reviewCount);

    private static JsonNode Written(object document) =>
        JsonNode.Parse(JsonSerializer.Serialize(document, AsTheApiWould))!;

    [Fact]
    public void A_product_declares_itself_in_schema_orgs_vocabulary()
    {
        var json = Written(StructuredData.Product(Address, Chair()));

        Assert.Equal("https://schema.org", (string?)json["@context"]);
        Assert.Equal("Product", (string?)json["@type"]);
        Assert.Equal("Oak Chair", (string?)json["name"]);
        Assert.Equal("FURN-OAK-CHAIR", (string?)json["sku"]);
        Assert.Equal("8594000000006", (string?)json["gtin13"]);
        Assert.Equal("214-OAK", (string?)json["mpn"]);
        Assert.Equal("https://acme.example/p/oak-chair", (string?)json["url"]);
        Assert.Equal("Brand", (string?)json["brand"]!["@type"]);
        Assert.Equal("Thonet", (string?)json["brand"]!["name"]);
    }

    // Every key is one schema.org reads. A document carrying an extra property of its own — a lower-case
    // "type" beside "@type", say — is not invalid, but it is this code leaking into somebody else's
    // vocabulary, and the only way to catch that is to name the whole set.
    [Fact]
    public void A_product_carries_no_keys_of_its_own_invention()
    {
        var keys = Written(StructuredData.Product(Address, Chair())).AsObject().Select(pair => pair.Key).Order();

        Assert.Equal(
            ["@context", "@type", "brand", "description", "gtin13", "image", "mpn", "name", "offers", "sku", "url"],
            keys);
    }

    [Fact]
    public void A_thing_with_no_picture_declares_no_pictures_rather_than_an_empty_list()
    {
        var json = Written(StructuredData.Product(Address, Chair() with { ImageUrls = [] }));

        Assert.False(json.AsObject().ContainsKey("image"));
    }

    [Fact]
    public void A_single_form_carries_one_offer_with_the_price_as_written()
    {
        var offers = Written(StructuredData.Product(Address, Chair()))["offers"]!;

        Assert.Equal("Offer", (string?)offers["@type"]);
        Assert.Equal("1299", (string?)offers["price"]);
        Assert.Equal("CZK", (string?)offers["priceCurrency"]);
        Assert.Equal("https://schema.org/InStock", (string?)offers["availability"]);
        Assert.Equal("https://acme.example/p/oak-chair", (string?)offers["url"]);
    }

    // Availability is what the warehouse says, not a field somebody maintains.
    [Fact]
    public void Nothing_left_is_declared_out_of_stock()
    {
        var offers = Written(StructuredData.Product(Address, Chair(available: 0)))["offers"]!;

        Assert.Equal("https://schema.org/OutOfStock", (string?)offers["availability"]);
    }

    // Several forms of one thing are several offers of it. Prices do not vary by form today, so the low and
    // the high are the same number and the count is what the document adds.
    [Fact]
    public void Several_forms_are_declared_as_an_aggregate()
    {
        var offers = Written(StructuredData.Product(Address, Chair(formCount: 3)))["offers"]!;

        Assert.Equal("AggregateOffer", (string?)offers["@type"]);
        Assert.Equal("1299", (string?)offers["lowPrice"]);
        Assert.Equal("1299", (string?)offers["highPrice"]);
        Assert.Equal(3, (int?)offers["offerCount"]);
        Assert.Null(offers["price"]);
    }

    [Fact]
    public void A_condition_nobody_has_stated_is_not_declared()
    {
        var stated = Written(StructuredData.Product(Address, Chair(condition: "Used")))["offers"]!;
        var unstated = Written(StructuredData.Product(Address, Chair()))["offers"]!;

        Assert.Equal("https://schema.org/UsedCondition", (string?)stated["itemCondition"]);
        Assert.Null(unstated["itemCondition"]);
    }

    // A shop with no reviews says nothing rather than declaring nought out of five, which is what a zero
    // would mean to anybody reading it (D-089).
    [Fact]
    public void A_product_nobody_has_reviewed_declares_no_rating()
    {
        var reviewed = Written(StructuredData.Product(Address, Chair(rating: 4.5m, reviewCount: 12)));
        var unreviewed = Written(StructuredData.Product(Address, Chair()));

        Assert.Equal("AggregateRating", (string?)reviewed["aggregateRating"]!["@type"]);
        Assert.Equal("4.5", (string?)reviewed["aggregateRating"]!["ratingValue"]);
        Assert.Equal(12, (int?)reviewed["aggregateRating"]!["reviewCount"]);
        Assert.Null(unreviewed["aggregateRating"]);
    }

    [Fact]
    public void A_thing_with_no_barcode_leaves_the_field_out_rather_than_sending_an_empty_one()
    {
        var json = Written(StructuredData.Product(Address, Chair(gtin: null)));

        Assert.False(json.AsObject().ContainsKey("gtin13"));
        Assert.Equal("FURN-OAK-CHAIR", (string?)json["sku"]);
    }

    // A price is written the way schema.org's examples write it, whatever the reader's culture does with a
    // decimal point.
    [Fact]
    public void A_price_is_written_with_a_point_and_no_thousands_separator()
    {
        var offers = Written(StructuredData.Product(Address, Chair() with { Price = 1234567.5m }))["offers"]!;

        Assert.Equal("1234567.5", (string?)offers["price"]);
    }

    [Fact]
    public void A_breadcrumb_trail_starts_at_the_shop_and_does_not_link_where_the_reader_already_is()
    {
        var json = Written(StructuredData.Breadcrumbs(
            Address,
            "Acme Furniture",
            [new Crumb("Furniture", "https://acme.example/c/furniture"), new Crumb("Chairs", "https://acme.example/c/chairs")]));
        var steps = json["itemListElement"]!.AsArray();

        Assert.Equal("BreadcrumbList", (string?)json["@type"]);
        Assert.Equal(3, steps.Count);
        Assert.Equal([1, 2, 3], steps.Select(step => (int?)step!["position"]));
        Assert.Equal(["Acme Furniture", "Furniture", "Chairs"], steps.Select(step => (string?)step!["name"]));
        Assert.Equal("https://acme.example/", (string?)steps[0]!["item"]);
        Assert.Null(steps[2]!["item"]);
    }

    [Fact]
    public void An_organization_declares_the_identity_an_invoice_names()
    {
        var seller = new SellerDetails("Acme Furniture s.r.o.", "Dřevařská 12", "Brno", "602 00", "CZ", "27654321", "CZ27654321");

        var json = Written(StructuredData.Organization(Address, "Acme Furniture", seller, "https://acme.example/api/storefront/store/logo"));

        Assert.Equal("Organization", (string?)json["@type"]);
        Assert.Equal("Acme Furniture", (string?)json["name"]);
        Assert.Equal("Acme Furniture s.r.o.", (string?)json["legalName"]);
        Assert.Equal("CZ27654321", (string?)json["vatID"]);
        Assert.Equal("27654321", (string?)json["taxID"]);
        Assert.Equal("PostalAddress", (string?)json["address"]!["@type"]);
        Assert.Equal("Brno", (string?)json["address"]!["addressLocality"]);
        Assert.Equal("CZ", (string?)json["address"]!["addressCountry"]);
    }

    // A shop that has not given its company details yet says who it is and no more, rather than declaring an
    // empty address (D-078).
    [Fact]
    public void A_shop_with_no_company_details_declares_no_address()
    {
        var json = Written(StructuredData.Organization(Address, "Acme Furniture", seller: null, logoUrl: null));

        Assert.Equal("Acme Furniture", (string?)json["name"]);
        Assert.False(json.AsObject().ContainsKey("address"));
        Assert.False(json.AsObject().ContainsKey("legalName"));
        Assert.False(json.AsObject().ContainsKey("logo"));
    }

    [Fact]
    public void A_website_declares_the_language_the_shop_is_written_in()
    {
        var json = Written(StructuredData.WebSite(Address, "Acme Furniture"));

        Assert.Equal("WebSite", (string?)json["@type"]);
        Assert.Equal("https://acme.example/", (string?)json["url"]);
        Assert.Equal("cs", (string?)json["inLanguage"]);
    }
}
