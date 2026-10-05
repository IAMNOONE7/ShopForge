using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Publishing;

// Writing down where a page used to live, from the one or two places a rename can happen (D-166).
internal static class SlugTrail
{
    public static async Task RecordAsync(
        DbContext dbContext,
        Guid storeId,
        SlugKind kind,
        string? wasCalled,
        string nowCalled,
        Guid pointsAt,
        DateTimeOffset movedAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(wasCalled) || wasCalled == nowCalled)
        {
            return;
        }

        // A slug this store gave up before, possibly by another row: the trail says where it goes now, not
        // everywhere it has ever been.
        var already = await dbContext.Set<SlugHistory>()
            .SingleOrDefaultAsync(history => history.Kind == kind && history.Slug == wasCalled, cancellationToken);

        if (already is not null)
        {
            already.NowPointsAt(pointsAt, movedAt);

            return;
        }

        dbContext.Add(new SlugHistory(storeId, kind, wasCalled, pointsAt, movedAt));
    }
}
