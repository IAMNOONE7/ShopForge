using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;

namespace ShopForge.Stores.Content;

internal static class StorefrontContentPageEndpoint
{
    public static IEndpointRouteBuilder MapStorefrontContentPages(this IEndpointRouteBuilder storefront)
    {
        storefront.MapGet("/pages/{slug}", GetPageAsync);

        return storefront;
    }

    private static async Task<Results<Ok<ContentPageResponse>, NotFound>> GetPageAsync(
        string slug,
        DbContext dbContext,
        ICurrentStoreSettings storeSettings,
        CancellationToken cancellationToken)
    {
        var page = await dbContext.Set<ContentPage>()
            .AsNoTracking()
            .SingleOrDefaultAsync(page => page.Slug == slug && page.IsPublished, cancellationToken);

        // A draft is not a page that moved; it is a page that does not exist yet, so a shopper is told the
        // same thing as for a name nobody has used.
        if (page is null)
        {
            return TypedResults.NotFound();
        }

        var settings = await storeSettings.GetAsync(cancellationToken);

        return TypedResults.Ok(new ContentPageResponse(
            page.Slug,
            page.Title,
            page.Body,
            PageMetadata.For(
                settings.Seo,
                page.Title,
                new PageSeoOverrides(page.SeoTitle, page.SeoDescription, null, page.SeoNoIndex),
                settings.Address?.ContentPage(page.Slug))));
    }
}

// The body is text, in paragraphs separated by blank lines. It is never markup, so whoever draws the page can
// put it on screen without sanitising anything (D-175).
internal sealed record ContentPageResponse(string Slug, string Title, string Body, PageSeo Seo);
