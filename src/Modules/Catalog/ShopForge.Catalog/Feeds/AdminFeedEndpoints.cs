using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Feeds;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Feeds;

// What a merchant does with a feed: turn it on, see how the last collection went, take the address to paste
// into somebody else's dashboard, produce one now, and change the token when they want to cut an engine off.
internal static class AdminFeedEndpoints
{
    public static IEndpointRouteBuilder MapAdminFeeds(this IEndpointRouteBuilder storeAdmin)
    {
        var feeds = storeAdmin.MapGroup("/feeds");

        feeds.MapGet("/", GetFeedsAsync);
        feeds.MapPut("/{feed}", SaveAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        feeds.MapPost("/{feed}/token", RotateAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        feeds.MapPost("/{feed}/run", RunAsync).RequireAuthorization(AdminPolicies.CatalogManagement).RequireRateLimiting(RateLimits.Expensive);

        return storeAdmin;
    }

    private static async Task<Ok<List<AdminFeedResponse>>> GetFeedsAsync(
        DbContext dbContext,
        IEnumerable<IProductFeedFormat> formats,
        IStoreUrls urls,
        CancellationToken cancellationToken)
    {
        var address = await urls.FindAsync(cancellationToken);
        var arrangements = await dbContext.Set<StoreFeed>().AsNoTracking().ToListAsync(cancellationToken);

        return TypedResults.Ok(formats
            .OrderBy(format => format.Key, StringComparer.Ordinal)
            .Select(format => AdminFeedResponse.From(format.Key, arrangements.SingleOrDefault(feed => feed.Feed == format.Key), address))
            .ToList());
    }

    private static async Task<Results<Ok<AdminFeedResponse>, ValidationProblem, NotFound>> SaveAsync(
        string feed,
        FeedRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IEnumerable<IProductFeedFormat> formats,
        IStoreUrls urls,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (formats.All(format => format.Key != feed))
        {
            return TypedResults.NotFound();
        }

        var arrangement = await dbContext.Set<StoreFeed>().SingleOrDefaultAsync(candidate => candidate.Feed == feed, cancellationToken);

        if (arrangement is null)
        {
            arrangement = new StoreFeed(storeContext.StoreId!.Value, feed, NewToken());
            dbContext.Add(arrangement);
        }

        arrangement.Switch(request.IsEnabled);
        audit.Record("feed.switched", feed, new { request.IsEnabled });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminFeedResponse.From(feed, arrangement, await urls.FindAsync(cancellationToken)));
    }

    // Cutting one engine off without disturbing the others, which is the whole reason the token is per
    // consumer rather than per store (D-148).
    private static async Task<Results<Ok<AdminFeedResponse>, NotFound>> RotateAsync(
        string feed,
        DbContext dbContext,
        IStoreUrls urls,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var arrangement = await dbContext.Set<StoreFeed>().SingleOrDefaultAsync(candidate => candidate.Feed == feed, cancellationToken);

        if (arrangement is null)
        {
            return TypedResults.NotFound();
        }

        arrangement.Rotate(NewToken());
        audit.Record("feed.token-rotated", feed);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminFeedResponse.From(feed, arrangement, await urls.FindAsync(cancellationToken)));
    }

    private static async Task<Results<Ok<AdminFeedResponse>, NotFound, ProblemHttpResult>> RunAsync(
        string feed,
        DbContext dbContext,
        IProductFeeds feeds,
        IStoreUrls urls,
        CancellationToken cancellationToken)
    {
        if (await feeds.RunAsync(feed, cancellationToken) is null)
        {
            var arrangement = await dbContext.Set<StoreFeed>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Feed == feed, cancellationToken);

            return arrangement is null
                ? TypedResults.NotFound()
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The feed could not be produced",
                    detail: arrangement.LastError ?? "The feed is switched off.");
        }

        var produced = await dbContext.Set<StoreFeed>().AsNoTracking().SingleAsync(candidate => candidate.Feed == feed, cancellationToken);

        return TypedResults.Ok(AdminFeedResponse.From(feed, produced, await urls.FindAsync(cancellationToken)));
    }

    private static string NewToken() => TokenValues.Create().Value;
}

internal sealed record FeedRequest(bool IsEnabled);

internal sealed record AdminFeedResponse(
    string Feed,
    bool IsEnabled,
    string? Url,
    DateTimeOffset? LastRunAt,
    int? LastMilliseconds,
    long? LastBytes,
    int? LastProducts,
    int? LastSkipped,
    string? LastError)
{
    public static AdminFeedResponse From(string feed, StoreFeed? arrangement, StoreAddress? address) =>
        arrangement is null
            ? new AdminFeedResponse(feed, IsEnabled: false, null, null, null, null, null, null, null)
            : new AdminFeedResponse(
                feed,
                arrangement.IsEnabled,

                // The address with the token in it, because that is what a merchant pastes into somebody
                // else's dashboard and typing it out themselves is how it goes wrong.
                address is null ? null : $"https://{address.Host}/api/storefront/feeds/{feed}.xml?token={Uri.EscapeDataString(arrangement.Token)}",
                arrangement.LastRunAt,
                arrangement.LastMilliseconds,
                arrangement.LastBytes,
                arrangement.LastProducts,
                arrangement.LastSkipped,
                arrangement.LastError);
}
