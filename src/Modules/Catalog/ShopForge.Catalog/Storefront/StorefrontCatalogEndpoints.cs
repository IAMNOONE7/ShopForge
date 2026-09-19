using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Images;
using ShopForge.Shared.Files;

namespace ShopForge.Catalog.Storefront;

internal static class StorefrontCatalogEndpoints
{
    private const int MaxPageSize = 100;

    public static void MapStorefrontCatalog(this IEndpointRouteBuilder storefront)
    {
        storefront.MapGet("/categories", GetCategoriesAsync);
        storefront.MapGet("/products", GetProductsAsync);
        storefront.MapGet("/products/{slug}", GetProductAsync);
        storefront.MapGet("/products/{storeProductId:guid}/images/{imageId:guid}", GetImageAsync);
    }

    private static async Task<Ok<List<StorefrontCategoryResponse>>> GetCategoriesAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var categories = await dbContext.Set<Category>()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new StorefrontCategoryResponse(category.Name, category.Slug))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(categories);
    }

    private static async Task<Results<Ok<ProductPageResponse>, NotFound>> GetProductsAsync(
        DbContext dbContext,
        string? category,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 24, 1, MaxPageSize);
        var storeProducts = dbContext.Set<StoreProduct>().Where(storeProduct => storeProduct.IsVisible);

        if (category is not null)
        {
            var categoryId = await dbContext.Set<Category>()
                .Where(candidate => candidate.Slug == category)
                .Select(candidate => (Guid?)candidate.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (categoryId is null)
            {
                return TypedResults.NotFound();
            }

            storeProducts = storeProducts.Where(storeProduct => storeProduct.Categories.Any(assignment => assignment.CategoryId == categoryId));
        }

        var totalCount = await storeProducts.CountAsync(cancellationToken);
        var items = await (
                from storeProduct in storeProducts
                join product in dbContext.Set<Product>() on storeProduct.ProductId equals product.Id
                orderby storeProduct.SortOrder, storeProduct.Name, storeProduct.Id
                select new
                {
                    storeProduct.Id,
                    storeProduct.Slug,
                    storeProduct.Name,
                    storeProduct.Price,
                    ImageId = product.Images.OrderBy(image => image.Position).Select(image => (Guid?)image.Id).FirstOrDefault(),
                })
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ProductPageResponse(
            items.Select(item => new ProductSummaryResponse(item.Id, item.Slug, item.Name, item.Price, ImageUrl(item.Id, item.ImageId))).ToList(),
            totalCount,
            pageNumber,
            size));
    }

    private static async Task<Results<Ok<ProductDetailResponse>, NotFound>> GetProductAsync(
        string slug,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var storeProduct = await dbContext.Set<StoreProduct>()
            .AsNoTracking()
            .Include(storeProduct => storeProduct.Categories)
            .SingleOrDefaultAsync(storeProduct => storeProduct.Slug == slug && storeProduct.IsVisible, cancellationToken);

        if (storeProduct is null)
        {
            return TypedResults.NotFound();
        }

        var product = await dbContext.Set<Product>().AsNoTracking().SingleAsync(product => product.Id == storeProduct.ProductId, cancellationToken);
        var categoryIds = storeProduct.Categories.Select(assignment => assignment.CategoryId).ToList();
        var categories = await dbContext.Set<Category>()
            .Where(category => categoryIds.Contains(category.Id))
            .OrderBy(category => category.SortOrder)
            .Select(category => new StorefrontCategoryResponse(category.Name, category.Slug))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ProductDetailResponse(
            storeProduct.Id,
            storeProduct.Slug,
            storeProduct.Name,
            storeProduct.Description,
            storeProduct.Price,
            product.Images
                .OrderBy(image => image.Position)
                .Select(image => new ProductImageResponse(ImageUrl(storeProduct.Id, image.Id)!, image.AltText))
                .ToList(),
            categories));
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> GetImageAsync(
        Guid storeProductId,
        Guid imageId,
        HttpContext httpContext,
        DbContext dbContext,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var filePath = await (
                from storeProduct in dbContext.Set<StoreProduct>()
                where storeProduct.Id == storeProductId && storeProduct.IsVisible
                join product in dbContext.Set<Product>() on storeProduct.ProductId equals product.Id
                from image in product.Images
                where image.Id == imageId
                select image.FilePath)
            .SingleOrDefaultAsync(cancellationToken);

        // Image files never change: a new upload gets a new id and therefore a new URL.
        httpContext.Response.Headers.CacheControl = "public, max-age=86400, immutable";

        return await ImageResults.StreamAsync(filePath, fileStorage, cancellationToken);
    }

    private static string? ImageUrl(Guid storeProductId, Guid? imageId) =>
        imageId is null ? null : $"/api/storefront/products/{storeProductId}/images/{imageId}";
}

internal sealed record StorefrontCategoryResponse(string Name, string Slug);

internal sealed record ProductPageResponse(List<ProductSummaryResponse> Items, int TotalCount, int Page, int PageSize);

internal sealed record ProductSummaryResponse(Guid Id, string Slug, string Name, decimal Price, string? ImageUrl);

internal sealed record ProductDetailResponse(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    decimal Price,
    List<ProductImageResponse> Images,
    List<StorefrontCategoryResponse> Categories);

internal sealed record ProductImageResponse(string Url, string? AltText);
