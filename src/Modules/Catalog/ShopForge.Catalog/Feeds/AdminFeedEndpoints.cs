using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Feeds.Google;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Feeds;
using ShopForge.Shared.Http;
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
        feeds.MapGet("/{feed}/check", CheckAsync);
        feeds.MapGet("/{feed}/categories", CategoriesAsync);
        feeds.MapPut("/{feed}/categories/{categoryId:guid}", MapCategoryAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
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

    private static async Task<Results<Ok<List<FeedProblem>>, NotFound>> CheckAsync(
        string feed,
        IProductFeeds feeds,
        CancellationToken cancellationToken) =>
        await feeds.CheckAsync(feed, cancellationToken) is { } problems
            ? TypedResults.Ok(problems)
            : TypedResults.NotFound();

    // Every category of the shop, with what this engine calls it where the merchant has said. The ones that
    // say nothing are the work still to do, and listing them all together is how it gets done once (D-170).
    private static async Task<Ok<List<MappedCategoryResponse>>> CategoriesAsync(
        string feed,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var mapped = await dbContext.Set<CategoryFeedMapping>()
            .AsNoTracking()
            .Where(mapping => mapping.Feed == feed)
            .ToDictionaryAsync(mapping => mapping.CategoryId, mapping => mapping.EngineCategory, cancellationToken);

        var categories = await dbContext.Set<Category>()
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(categories
            .Select(category => new MappedCategoryResponse(category.Id, category.Name, mapped.GetValueOrDefault(category.Id)))
            .ToList());
    }

    private static async Task<Results<Ok<MappedCategoryResponse>, ValidationProblem, NotFound>> MapCategoryAsync(
        string feed,
        Guid categoryId,
        MapCategoryRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IEnumerable<IProductFeedFormat> formats,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Set<Category>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null || formats.All(format => format.Key != feed))
        {
            return TypedResults.NotFound();
        }

        var errors = new RequestErrors().Check(
            request.EngineCategory is null || request.EngineCategory.Trim().Length <= CategoryFeedMapping.MaxCategoryLength,
            "engineCategory",
            $"A category can be up to {CategoryFeedMapping.MaxCategoryLength} characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var mapping = await dbContext.Set<CategoryFeedMapping>()
            .SingleOrDefaultAsync(candidate => candidate.CategoryId == categoryId && candidate.Feed == feed, cancellationToken);

        // Emptied means unmapped, which is a thing a merchant may want to do after getting it wrong.
        if (string.IsNullOrWhiteSpace(request.EngineCategory))
        {
            if (mapping is not null)
            {
                dbContext.Remove(mapping);
            }
        }
        else if (mapping is null)
        {
            dbContext.Add(new CategoryFeedMapping(storeContext.StoreId!.Value, categoryId, feed, request.EngineCategory));
        }
        else
        {
            mapping.Rename(request.EngineCategory);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new MappedCategoryResponse(categoryId, category.Name, request.EngineCategory?.Trim()));
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

internal sealed record MapCategoryRequest(string? EngineCategory);

internal sealed record MappedCategoryResponse(Guid CategoryId, string Name, string? EngineCategory);

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
