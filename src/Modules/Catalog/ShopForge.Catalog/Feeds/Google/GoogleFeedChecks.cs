using ShopForge.Shared.Feeds;

namespace ShopForge.Catalog.Feeds.Google;

// What Merchant Center refuses, said before it says it. A merchant who learns three days later that half their
// catalogue was rejected has lost three days; the rules are published, so the shop can read them out (D-169).
internal static class GoogleFeedChecks
{
    public static IReadOnlyList<string> Problems(FeedProduct product)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(product.Name))
        {
            problems.Add("It has no title.");
        }

        if (string.IsNullOrWhiteSpace(product.Description))
        {
            problems.Add("It has no description, so the title is sent instead, which Google may reject as too short.");
        }

        if (product.ImageUrl is null)
        {
            problems.Add("It has no image, which Google requires.");
        }

        if (product.Price <= 0)
        {
            problems.Add("Its price is zero, which Google rejects.");
        }

        if (string.IsNullOrWhiteSpace(product.Brand))
        {
            problems.Add("It has no brand, which Google requires for almost every category.");
        }

        // Not a rejection on its own — the feed says so plainly — but a product nobody can match to anybody
        // else's listing competes worse, and a merchant should know which of theirs those are.
        if (product.Gtin is null && product.PartNumber is null)
        {
            problems.Add("It has neither a barcode nor a part number, so it is sent as having no identifier.");
        }

        return problems;
    }
}

internal sealed record FeedProblem(string Sku, string Name, IReadOnlyList<string> Problems);
