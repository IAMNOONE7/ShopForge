using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Catalog.Search;

// A year of searches is enough to see a season twice and compare it. Past that the report is history rather
// than a thing to act on, and keeping what people typed forever is not a habit worth having even when none of
// it names anybody (D-178).
internal sealed class SearchLogCleanup(DbContext dbContext, TimeProvider clock) : IStoreMaintenance
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(366);

    public string Name => "Search log";

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - Lifetime;

        return dbContext.Set<SearchQuery>().Where(query => query.AskedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
