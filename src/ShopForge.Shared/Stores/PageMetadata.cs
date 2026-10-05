namespace ShopForge.Shared.Stores;

// What a store says about its pages when a page says nothing of its own: a suffix to put after a page's name,
// a description to fall back on, an image for whoever shares a link, and a switch for a shop that is not ready
// to be found at all (D-165).
public sealed record StoreSeo(string? TitleSuffix, string? Description, string? SocialImageUrl, bool NoIndex)
{
    public static readonly StoreSeo None = new(null, null, null, NoIndex: false);
}

// What one page says, after the fallbacks have been followed. Nothing renders this yet; it is what the
// storefront answers with and what sitemaps, structured data and the head will read later.
public sealed record PageSeo(string Title, string? Description, string? SocialImageUrl, bool NoIndex);

// The fallback chain, in one place and with no dependencies, so every page kind answers the same way.
public static class PageMetadata
{
    public static PageSeo For(StoreSeo store, string pageName, PageSeoOverrides page) => new(
        // A merchant who writes a title means that exact title; the suffix is for the ones they have not
        // written, where it turns "Oak Chair" into "Oak Chair — Acme Furniture".
        Title: Clean(page.Title) ?? WithSuffix(pageName, store.TitleSuffix),
        Description: Clean(page.Description) ?? Clean(store.Description),
        SocialImageUrl: Clean(page.SocialImageUrl) ?? Clean(store.SocialImageUrl),

        // A shop that is not ready to be found hides every page of itself, whatever a page says; a page can
        // hide itself in a shop that is otherwise open. Neither can overrule the other into being indexed.
        NoIndex: store.NoIndex || page.NoIndex);

    private static string WithSuffix(string pageName, string? suffix) =>
        Clean(suffix) is { } tail ? $"{pageName} — {tail}" : pageName;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// What one page has been given of its own. All absent is the ordinary case.
public sealed record PageSeoOverrides(string? Title, string? Description, string? SocialImageUrl, bool NoIndex)
{
    public static readonly PageSeoOverrides None = new(null, null, null, NoIndex: false);
}
