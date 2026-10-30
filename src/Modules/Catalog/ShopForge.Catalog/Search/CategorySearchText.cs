using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Search;

// The words a category's listings are found by: its name and keywords and those of every live category above it,
// kept on the category as one text, so the search index reads a listing's categories without walking the tree
// (D-226). The whole tree is read again after any change to it: a store has a few hundred categories at most, and one
// rename reaches everything beneath it.
internal sealed class CategorySearchText(DbContext dbContext, SearchIndex index)
{
    // Writes every category's text as the tree now stands, and says which changed.
    public async Task<IReadOnlyList<Guid>> RewriteAsync(CancellationToken cancellationToken)
    {
        var categories = await dbContext.Set<Category>().IgnoreQueryFilters([TenancyFilters.Archived]).ToListAsync(cancellationToken);
        var byId = categories.ToDictionary(category => category.Id);
        var changed = categories.Where(category => category.SetSearchText(TextOf(category, byId))).Select(category => category.Id).ToList();

        if (changed.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    // The same, and then every listing filed under a category whose words changed is indexed again.
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var changed = await RewriteAsync(cancellationToken);

        if (changed.Count == 0)
        {
            return;
        }

        var listings = await dbContext.Set<StoreProduct>()
            .Where(listing => listing.Categories.Any(assignment => changed.Contains(assignment.CategoryId)))
            .Select(listing => listing.Id)
            .ToListAsync(cancellationToken);

        await index.RefreshAsync(listings, cancellationToken);
    }

    // An archived category finds nothing, and its words leave the categories beneath it too. The walk stops after the
    // deepest tree the shop allows, so a loop, which the tree's rules forbid, could not hold it.
    private static string? TextOf(Category category, Dictionary<Guid, Category> byId)
    {
        if (category.IsArchived)
        {
            return null;
        }

        var words = new List<string>();
        Category? current = category;

        for (var depth = 0; current is not null && depth < CategoryTree.MaxDepth; depth++)
        {
            if (!current.IsArchived)
            {
                words.Add(current.Name);

                if (current.SearchKeywords is { } keywords)
                {
                    words.Add(keywords);
                }
            }

            current = current.ParentId is { } parent ? byId.GetValueOrDefault(parent) : null;
        }

        return string.Join(" ", words);
    }
}
