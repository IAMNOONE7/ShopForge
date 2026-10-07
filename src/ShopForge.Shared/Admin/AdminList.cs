namespace ShopForge.Shared.Admin;

// Every admin list answers the same way: which page, how big, how ordered, and what the merchant was looking
// for. One shape across the modules, because a screen that pages orders and a screen that pages products
// should not need two different ideas of what a page is (D-179).
public sealed record AdminListQuery(int Page, int PageSize, string? Sort, string? Terms)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    // Short enough that a code or a surname works, long enough not to match most of a catalogue.
    public const int ShortestTerms = 2;

    public static AdminListQuery Of(int? page, int? pageSize, string? sort, string? terms)
    {
        var typed = terms?.Trim();

        return new AdminListQuery(
            Math.Max(page ?? 1, 1),
            Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize),
            string.IsNullOrWhiteSpace(sort) ? null : sort.Trim(),
            typed is { Length: >= ShortestTerms } ? typed : null);
    }

    // Written for a `LIKE` against a lowered column, so the match is case-insensitive without reaching for a
    // provider's own operator. The characters that mean something to `LIKE` are escaped, so a merchant
    // searching for "50%" is looking for a per cent sign rather than for everything.
    public string? Pattern => Terms is null
        ? null
        : $"%{Terms.ToLowerInvariant().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";

    // The arithmetic of a page, named here so that every list does it the same way. The helper that would
    // apply it cannot live here: `Shared` carries contracts and no ORM, and counting rows needs one (D-179).
    public int Skipped => (Page - 1) * PageSize;

    public int Taken => PageSize;

    public bool Descending => Sort?.StartsWith('-') == true;

    public string? SortKey => Sort is null ? null : Descending ? Sort[1..] : Sort;
}

// A page of a list, and enough to draw the paging beside it. The total is the whole list, not the page, which
// is what lets a screen say "page 3 of 40" rather than "there may be more".
public sealed record AdminListResponse<T>(List<T> Items, int TotalCount, int Page, int PageSize)
{
    public bool HasMore => (long)Page * PageSize < TotalCount;
}
