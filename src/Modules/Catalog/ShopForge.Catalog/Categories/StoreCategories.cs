using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Categories;

// A shop's categories, read in one go. A page used to read the one category it was asked for; now it reads
// them all and finds that one in memory, which costs the same single query and answers what is above it, what
// is beneath it and what sits directly under it without going back for any of them (D-172).
internal static class StoreCategories
{
    public static async Task<StoreCategoryTree> ReadAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<Category>()
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryRow(
                category.Id,
                category.ParentId,
                category.Name,
                category.Slug,
                category.SeoTitle,
                category.SeoDescription,
                category.PageText))
            .ToListAsync(cancellationToken);

        return new StoreCategoryTree(rows);
    }

    // Only the shape, for whoever needs to know where a category sits and nothing about what it says.
    public static async Task<CategoryTree> ShapeAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var places = await dbContext.Set<Category>()
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryPlace(category.Id, category.ParentId))
            .ToListAsync(cancellationToken);

        return CategoryTree.Of(places);
    }
}

internal sealed record CategoryRow(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string? SeoTitle,
    string? SeoDescription,
    string? PageText);

// The rows and their shape together, since every caller that wants one wants the other.
internal sealed class StoreCategoryTree(IReadOnlyList<CategoryRow> rows)
{
    private readonly Dictionary<Guid, CategoryRow> _byId = rows.ToDictionary(row => row.Id);

    public CategoryTree Shape { get; } = CategoryTree.Of([.. rows.Select(row => new CategoryPlace(row.Id, row.ParentId))]);

    public CategoryRow? Called(string slug) => rows.SingleOrDefault(row => row.Slug == slug);

    public CategoryRow Row(Guid id) => _byId[id];

    public IReadOnlyList<CategoryRow> InTreeOrder() => [.. Shape.Everything().Select(Row)];

    // Root first, this one last: the crumbs a breadcrumb is drawn from.
    public IReadOnlyList<CategoryRow> PathTo(Guid id) => [.. Shape.PathTo(id).Select(Row)];

    public IReadOnlyList<CategoryRow> ChildrenOf(Guid id) => [.. Shape.ChildrenOf(id).Select(Row)];
}
