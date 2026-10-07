using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Content;

// A page of a shop's own words: its terms, its privacy notice, what it charges for delivery, who it is. A page
// with text and not a CMS — no articles, no authorship, no scheduling, no versions (D-175).
//
// The body is text, never markup. The e-mail layout learned in 19b that merchant words are encoded before they
// reach HTML, and the cheapest way to keep that true is to have nothing to sanitise: the shop stores
// paragraphs, the API hands them over as text, and whoever draws the page decides what a blank line looks
// like.
internal sealed class ContentPage : IStoreOwned
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 20_000;

    private ContentPage()
    {
    }

    public ContentPage(Guid storeId, string slug, string title, string body)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Update(slug, title, body);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Slug { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    // A page nobody has published is a draft: it is not served, and it is not advertised to a crawler.
    public bool IsPublished { get; private set; }

    public string? SeoTitle { get; private set; }

    public string? SeoDescription { get; private set; }

    public bool SeoNoIndex { get; private set; }

    public void Update(string slug, string title, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (!Slugs.IsValid(slug))
        {
            throw new ArgumentException($"'{slug}' is not a valid slug.", nameof(slug));
        }

        Slug = slug;
        Title = title.Trim();
        Body = body?.Trim() ?? string.Empty;
    }

    public void DescribeToSearchEngines(string? title, string? description, bool noIndex)
    {
        SeoTitle = Tidied(title);
        SeoDescription = Tidied(description);
        SeoNoIndex = noIndex;
    }

    public void Publish() => IsPublished = true;

    public void Unpublish() => IsPublished = false;

    private static string? Tidied(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
