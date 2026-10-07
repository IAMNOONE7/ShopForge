using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Content;

internal static class AdminContentPageEndpoints
{
    public static IEndpointRouteBuilder MapAdminContentPages(this IEndpointRouteBuilder storeAdmin)
    {
        var pages = storeAdmin.MapGroup("/pages").RequireAuthorization(AdminPolicies.StoreManagement);

        pages.MapGet("/", GetPagesAsync);
        pages.MapPost("/", CreateAsync);
        pages.MapPut("/{pageId:guid}", UpdateAsync);
        pages.MapDelete("/{pageId:guid}", DeleteAsync);

        return storeAdmin;
    }

    private static async Task<Ok<List<AdminContentPageResponse>>> GetPagesAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<ContentPage>()
            .AsNoTracking()
            .OrderBy(page => page.Title)
            .Select(page => Describe(page))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<AdminContentPageResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        ContentPageRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var slug = Slug(request);
        var errors = Validate(request, slug);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<ContentPage>().AnyAsync(existing => existing.Slug == slug, cancellationToken))
        {
            return SlugTaken();
        }

        var page = new ContentPage(storeContext.StoreId!.Value, slug, request.Title!, request.Body ?? string.Empty);
        Describe(page, request);

        dbContext.Add(page);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{page.StoreId}/pages/{page.Id}", Describe(page));
    }

    private static async Task<Results<Ok<AdminContentPageResponse>, ValidationProblem, ProblemHttpResult, NotFound>> UpdateAsync(
        Guid pageId,
        ContentPageRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var page = await dbContext.Set<ContentPage>().SingleOrDefaultAsync(page => page.Id == pageId, cancellationToken);

        if (page is null)
        {
            return TypedResults.NotFound();
        }

        var slug = request.Slug ?? page.Slug;
        var errors = Validate(request, slug);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<ContentPage>().AnyAsync(other => other.Slug == slug && other.Id != pageId, cancellationToken))
        {
            return SlugTaken();
        }

        page.Update(slug, request.Title!, request.Body ?? string.Empty);
        Describe(page, request);

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(Describe(page));
    }

    // A page with text in it and nothing pointing at it: there is nothing to keep once a merchant says so,
    // which is why this is a delete rather than another kind of draft.
    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid pageId, DbContext dbContext, CancellationToken cancellationToken)
    {
        var page = await dbContext.Set<ContentPage>().SingleOrDefaultAsync(page => page.Id == pageId, cancellationToken);

        if (page is null)
        {
            return TypedResults.NotFound();
        }

        dbContext.Remove(page);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static void Describe(ContentPage page, ContentPageRequest request)
    {
        if (request.IsPublished)
        {
            page.Publish();
        }
        else
        {
            page.Unpublish();
        }

        page.DescribeToSearchEngines(request.Seo?.Title, request.Seo?.Description, request.Seo?.NoIndex ?? false);
    }

    private static string Slug(ContentPageRequest request) =>
        request.Slug ?? (request.Title is null ? string.Empty : Slugs.Create(request.Title));

    private static RequestErrors Validate(ContentPageRequest request, string slug) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(request.Title) && request.Title.Trim().Length <= ContentPage.MaxTitleLength,
                "title", $"A title is required (up to {ContentPage.MaxTitleLength} characters).")
            .Check(Slugs.IsValid(slug), "slug", "Slug may contain lower-case letters, digits and single hyphens.")
            .Check((request.Body ?? string.Empty).Length <= ContentPage.MaxBodyLength,
                "body", $"A page can hold up to {ContentPage.MaxBodyLength} characters.");

    private static ProblemHttpResult SlugTaken() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The slug is already used in this store");

    private static AdminContentPageResponse Describe(ContentPage page) => new(
        page.Id,
        page.Slug,
        page.Title,
        page.Body,
        page.IsPublished,
        page.SeoTitle,
        page.SeoDescription,
        page.SeoNoIndex);
}

internal sealed record ContentPageRequest(string? Slug, string? Title, string? Body, bool IsPublished, ContentPageSeoRequest? Seo = null);

internal sealed record ContentPageSeoRequest(string? Title, string? Description, bool NoIndex);

internal sealed record AdminContentPageResponse(
    Guid Id,
    string Slug,
    string Title,
    string Body,
    bool IsPublished,
    string? SeoTitle,
    string? SeoDescription,
    bool SeoNoIndex);
