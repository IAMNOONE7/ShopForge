using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Files;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Feeds;

internal static class FeedEndpoints
{
    public static IEndpointRouteBuilder MapProductFeeds(this IEndpointRouteBuilder storefront)
    {
        // A shopping engine collects this on a schedule of its own and is not a browser; it shares the window
        // crawlers use rather than the one a shopper writes carts with (D-126, D-167).
        storefront.MapGet("/feeds/{feed}.xml", CollectAsync).RequireRateLimiting(RateLimits.Crawlers);

        return storefront;
    }

    // Streamed from storage rather than produced here: a feed for a shop with fifty thousand products is a
    // document of several megabytes, and building one per collection is what the load baseline exists to
    // catch (D-168).
    private static async Task<Results<FileStreamHttpResult, NotFound, UnauthorizedHttpResult>> CollectAsync(
        string feed,
        string? token,
        DbContext dbContext,
        IFileStorage files,
        CancellationToken cancellationToken)
    {
        var arrangement = await dbContext.Set<StoreFeed>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Feed == feed && candidate.IsEnabled, cancellationToken);

        if (arrangement is null)
        {
            return TypedResults.NotFound();
        }

        // The token is this engine's and this shop's. A wrong one is told nothing about whether the feed
        // exists, which is why the comparison is in constant time and the answer is the same either way.
        if (token is null || !Tokens.Match(token, arrangement.Token))
        {
            return TypedResults.Unauthorized();
        }

        if (arrangement.FilePath is null || await files.OpenReadAsync(arrangement.FilePath, cancellationToken) is not { } stored)
        {
            // Switched on but never produced: there is nothing to collect yet rather than something wrong.
            return TypedResults.NotFound();
        }

        return TypedResults.Stream(stored.Content, stored.ContentType, $"{feed}.xml");
    }
}
