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
//
// The canonical is the page's one true address, and is null only for a shop with no proved domain of its own:
// every address here is absolute, because naming a relative one leaves the question this field exists to
// answer — which host — open (D-174).
public sealed record PageSeo(string Title, string? Description, string? SocialImageUrl, bool NoIndex, string? Canonical = null);

// The fallback chain, in one place and with no dependencies, so every page kind answers the same way.
public static class PageMetadata
{
    public static PageSeo For(StoreSeo store, string pageName, PageSeoOverrides page, string? canonical = null) => new(
        // A merchant who writes a title means that exact title; the suffix is for the ones they have not
        // written, where it turns "Oak Chair" into "Oak Chair — Acme Furniture".
        Title: Clean(page.Title) ?? WithSuffix(pageName, store.TitleSuffix),
        Description: Clean(page.Description) ?? Clean(store.Description),
        SocialImageUrl: Clean(page.SocialImageUrl) ?? Clean(store.SocialImageUrl),

        // A shop that is not ready to be found hides every page of itself, whatever a page says; a page can
        // hide itself in a shop that is otherwise open. Neither can overrule the other into being indexed.
        NoIndex: store.NoIndex || page.NoIndex,
        Canonical: canonical);

    private static string WithSuffix(string pageName, string? suffix) =>
        Clean(suffix) is { } tail ? $"{pageName} — {tail}" : pageName;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// What one page has been given of its own. All absent is the ordinary case.
public sealed record PageSeoOverrides(string? Title, string? Description, string? SocialImageUrl, bool NoIndex)
{
    public static readonly PageSeoOverrides None = new(null, null, null, NoIndex: false);
}

// Which address of the several a list can be reached by is the one worth indexing, and how a reader walks from
// one page of it to the next. One place, because a canonical that disagrees with a prev/next link is two
// answers to the same question (D-174).
//
// Everything here works on the clean absolute address of the list — the category, or the shop's front page —
// and the page number the reader asked for.
public static class PagedPages
{
    // A page of a list is the list plus which page, and page one is the list itself: `?page=1` is a second
    // address for a page that already has one.
    public static string At(string listUrl, int page) => page <= 1 ? listUrl : $"{listUrl}?page={page}";

    // A filtered or sorted list is the same products chosen or ordered differently, so it is not a page of
    // anything — it points at the list itself and takes the page number off with it.
    public static string Canonical(string listUrl, int page, bool narrowedOrReordered) =>
        narrowedOrReordered ? listUrl : At(listUrl, page);

    public static string? Previous(string listUrl, int page, bool narrowedOrReordered) =>
        narrowedOrReordered || page <= 1 ? null : At(listUrl, page - 1);

    // There is a next page when this one does not reach the end of the list. A list nobody has narrowed is
    // the only sequence worth walking, for the same reason it is the only one worth indexing.
    public static string? Next(string listUrl, int page, int pageSize, int totalCount, bool narrowedOrReordered) =>
        narrowedOrReordered || (long)page * pageSize >= totalCount ? null : At(listUrl, page + 1);
}
