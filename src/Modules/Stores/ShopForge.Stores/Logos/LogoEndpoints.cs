using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Files;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Logos;

internal static class LogoEndpoints
{
    public static void MapAdminStoreLogo(this IEndpointRouteBuilder storeAdmin)
    {
        // Cookie auth is SameSite=Strict, so cross-site form posts never carry the session; antiforgery tokens add nothing.
        storeAdmin.MapPut("/logo", ReplaceLogoAsync)
            .RequireAuthorization(AdminPolicies.StoreManagement)
            .DisableAntiforgery();
        storeAdmin.MapGet("/logo", GetLogoAsync);
    }

    public static void MapStorefrontLogo(this IEndpointRouteBuilder storefront) =>
        storefront.MapGet("/store/logo", GetStorefrontLogoAsync);

    private static async Task<Results<NoContent, ValidationProblem>> ReplaceLogoAsync(
        IFormFile? file,
        DbContext dbContext,
        IStoreContext storeContext,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        if (file is not { Length: > 0 } || file.Length > ImageFormats.MaxBytes)
        {
            return InvalidFile("A logo image of at most 5 MB is required.");
        }

        await using var content = file.OpenReadStream();
        var contentType = await ImageFormats.DetectAsync(content, cancellationToken);

        if (contentType is null)
        {
            return InvalidFile("Only JPEG, PNG and WebP images are supported.");
        }

        var store = await dbContext.Set<Store>().SingleAsync(store => store.Id == storeContext.StoreId, cancellationToken);
        var previousPath = store.ReplaceLogo(contentType);

        await fileStorage.SaveAsync(store.LogoPath!, content, contentType, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (previousPath is not null)
        {
            await fileStorage.DeleteAsync(previousPath, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> GetStorefrontLogoAsync(
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var result = await GetLogoAsync(dbContext, storeContext, fileStorage, cancellationToken);

        if (result.Result is FileStreamHttpResult)
        {
            httpContext.Response.Headers.CacheControl = "public, max-age=300";
        }

        return result;
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> GetLogoAsync(
        DbContext dbContext,
        IStoreContext storeContext,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var logoPath = await dbContext.Set<Store>()
            .Where(store => store.Id == storeContext.StoreId)
            .Select(store => store.LogoPath)
            .SingleOrDefaultAsync(cancellationToken);

        var file = logoPath is null ? null : await fileStorage.OpenReadAsync(logoPath, cancellationToken);

        return file is null ? TypedResults.NotFound() : TypedResults.Stream(file.Content, file.ContentType);
    }

    private static ValidationProblem InvalidFile(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
}
