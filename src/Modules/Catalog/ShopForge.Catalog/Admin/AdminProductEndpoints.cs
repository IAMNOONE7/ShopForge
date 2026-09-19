using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Images;
using ShopForge.Shared.Files;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Admin;

internal static class AdminProductEndpoints
{
    public static void MapAdminProducts(this IEndpointRouteBuilder tenantAdmin)
    {
        var products = tenantAdmin.MapGroup("/products");

        products.MapGet("/", GetProductsAsync);
        products.MapPost("/", CreateProductAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        products.MapPut("/{productId:guid}", UpdateProductAsync).RequireAuthorization(AdminPolicies.CatalogManagement);

        // Cookie auth is SameSite=Strict, so cross-site form posts never carry the session; antiforgery tokens add nothing.
        products.MapPost("/{productId:guid}/images", UploadImageAsync)
            .RequireAuthorization(AdminPolicies.CatalogManagement)
            .DisableAntiforgery();
        products.MapDelete("/{productId:guid}/images/{imageId:guid}", DeleteImageAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        products.MapGet("/{productId:guid}/images/{imageId:guid}", GetImageAsync);
    }

    private static async Task<Ok<List<AdminProductResponse>>> GetProductsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var products = await dbContext.Set<Product>().AsNoTracking().OrderBy(product => product.Sku).ToListAsync(cancellationToken);

        return TypedResults.Ok(products.Select(AdminProductResponse.From).ToList());
    }

    private static async Task<Results<Created<AdminProductResponse>, ValidationProblem, ProblemHttpResult>> CreateProductAsync(
        CreateProductRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(request.Sku) && request.Sku.Trim().Length <= 64, "sku", "SKU is required (up to 64 characters).")
            .Check(IsValidEan(request.Ean), "ean", "EAN must have 8 to 14 digits.")
            .Check(request.WeightGrams is null or >= 0, "weightGrams", "Weight cannot be negative.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var product = new Product(storeContext.TenantId!.Value, request.Sku!, request.Ean, request.WeightGrams);

        if (await dbContext.Set<Product>().AnyAsync(existing => existing.Sku == product.Sku, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A product with this SKU already exists");
        }

        dbContext.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/products/{product.Id}", AdminProductResponse.From(product));
    }

    private static async Task<Results<Ok<AdminProductResponse>, ValidationProblem, NotFound>> UpdateProductAsync(
        Guid productId,
        UpdateProductRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(IsValidEan(request.Ean), "ean", "EAN must have 8 to 14 digits.")
            .Check(request.WeightGrams is null or >= 0, "weightGrams", "Weight cannot be negative.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null)
        {
            return TypedResults.NotFound();
        }

        product.UpdatePhysicalData(request.Ean, request.WeightGrams);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminProductResponse.From(product));
    }

    private static async Task<Results<Created<AdminImageResponse>, ValidationProblem, NotFound>> UploadImageAsync(
        Guid productId,
        IFormFile? file,
        [FromForm] string? altText,
        DbContext dbContext,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(file is { Length: > 0 }, "file", "An image file is required.")
            .Check(file is null || file.Length <= ImageFormats.MaxBytes, "file", "Images can be at most 5 MB.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        await using var content = file!.OpenReadStream();
        var contentType = await ImageFormats.DetectAsync(content, cancellationToken);

        if (contentType is null)
        {
            return new RequestErrors().Check(false, "file", "Only JPEG, PNG and WebP images are supported.").ToProblem();
        }

        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var image = product.AddImage(contentType, altText);

        // The file goes first: if saving the row fails, an orphaned file is harmless, a row without a file is not.
        await fileStorage.SaveAsync(image.FilePath, content, contentType, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/products/{productId}/images/{image.Id}", AdminImageResponse.From(productId, image));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteImageAsync(
        Guid productId,
        Guid imageId,
        DbContext dbContext,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);
        var image = product?.RemoveImage(imageId);

        if (image is null)
        {
            return TypedResults.NotFound();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await fileStorage.DeleteAsync(image.FilePath, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> GetImageAsync(
        Guid productId,
        Guid imageId,
        DbContext dbContext,
        IFileStorage fileStorage,
        CancellationToken cancellationToken)
    {
        var filePath = await dbContext.Set<Product>()
            .Where(product => product.Id == productId)
            .SelectMany(product => product.Images)
            .Where(image => image.Id == imageId)
            .Select(image => image.FilePath)
            .SingleOrDefaultAsync(cancellationToken);

        return await ImageResults.StreamAsync(filePath, fileStorage, cancellationToken);
    }

    private static bool IsValidEan(string? ean) =>
        string.IsNullOrWhiteSpace(ean) || ean.Trim() is { Length: >= 8 and <= 14 } trimmed && trimmed.All(char.IsAsciiDigit);
}

internal sealed record CreateProductRequest(string? Sku, string? Ean, int? WeightGrams);

internal sealed record UpdateProductRequest(string? Ean, int? WeightGrams);

internal sealed record AdminProductResponse(Guid Id, string Sku, string? Ean, int? WeightGrams, List<AdminImageResponse> Images)
{
    public static AdminProductResponse From(Product product) => new(
        product.Id,
        product.Sku,
        product.Ean,
        product.WeightGrams,
        product.Images.OrderBy(image => image.Position).Select(image => AdminImageResponse.From(product.Id, image)).ToList());
}

internal sealed record AdminImageResponse(Guid Id, string Url, string? AltText, int Position)
{
    public static AdminImageResponse From(Guid productId, ProductImage image) =>
        new(image.Id, $"/api/admin/products/{productId}/images/{image.Id}", image.AltText, image.Position);
}
