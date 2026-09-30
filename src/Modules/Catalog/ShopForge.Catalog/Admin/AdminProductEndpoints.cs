using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Images;
using ShopForge.Shared.Files;
using ShopForge.Shared.Http;
using ShopForge.Shared.Platform;
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
        products.MapPut("/{productId:guid}/options", SetOptionsAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        products.MapPost("/{productId:guid}/variants", AddVariantAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        products.MapPut("/{productId:guid}/variants/{variantId:guid}", UpdateVariantAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        products.MapDelete("/{productId:guid}/variants/{variantId:guid}", RemoveVariantAsync).RequireAuthorization(AdminPolicies.CatalogManagement);

        // Cookie auth is SameSite=Strict, so cross-site form posts never carry the session; antiforgery tokens add nothing.
        products.MapPost("/{productId:guid}/images", UploadImageAsync)
            .RequireAuthorization(AdminPolicies.CatalogManagement)
            .DisableAntiforgery();
        products.MapDelete("/{productId:guid}/images/{imageId:guid}", DeleteImageAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        products.MapGet("/{productId:guid}/images/{imageId:guid}", GetImageAsync);
    }

    private static async Task<Ok<List<AdminProductResponse>>> GetProductsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var products = await dbContext.Set<Product>().AsNoTracking().ToListAsync(cancellationToken);

        return TypedResults.Ok(products.Select(AdminProductResponse.From).OrderBy(product => product.Sku, StringComparer.Ordinal).ToList());
    }

    private static async Task<Results<Created<AdminProductResponse>, ValidationProblem, ProblemHttpResult>> CreateProductAsync(
        CreateProductRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        ITenantLimits limits,
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

        if (await Plans.RefusedAsync(dbContext, limits, adding: 1, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var product = new Product(storeContext.TenantId!.Value, request.Sku!, request.Ean, request.WeightGrams);

        if (await TakenAsync(dbContext, product.Default.Sku, cancellationToken))
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

        // A product sold in several forms has a barcode and a weight per form, so there is nothing here to set.
        if (product.Variants.Count > 1)
        {
            return new RequestErrors()
                .Check(false, "weightGrams", "This product is sold in several forms; set the barcode and weight on each one.")
                .ToProblem();
        }

        product.Default.UpdatePhysicalData(request.Ean, request.WeightGrams);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminProductResponse.From(product));
    }

    private static async Task<Results<Ok<AdminProductResponse>, ValidationProblem, NotFound>> SetOptionsAsync(
        Guid productId,
        SetProductOptionsRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var names = Cleaned(request.Names);
        var chosen = request.Values ?? new Dictionary<Guid, IReadOnlyList<string>>();
        var errors = new RequestErrors()
            .Check(names.Length <= Product.MaxOptions, "names", $"A product can be sold along at most {Product.MaxOptions} axes.")
            .Check(names.Length == (request.Names?.Count ?? 0), "names", "An axis needs a name.")
            .Check(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length, "names", "Two axes cannot have the same name.")
            .Check(
                product.Variants.All(variant => chosen.TryGetValue(variant.Id, out var values) && Cleaned(values).Length == names.Length),
                "values",
                "Every form of the product needs one value for each axis.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        product.SetOptions(names, product.Variants.ToDictionary(variant => variant.Id, variant => Cleaned(chosen[variant.Id])));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminProductResponse.From(product));
    }

    private static async Task<Results<Created<AdminVariantResponse>, ValidationProblem, NotFound, ProblemHttpResult>> AddVariantAsync(
        Guid productId,
        VariantRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null)
        {
            return TypedResults.NotFound();
        }

        if (Refused(request, product) is { } errors)
        {
            return errors;
        }

        if (await TakenAsync(dbContext, ProductVariant.Normalise(request.Sku!), cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A product with this SKU already exists");
        }

        var variant = product.AddVariant(request.Sku!, request.Ean, request.WeightGrams, Cleaned(request.OptionValues));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/products/{productId}/variants/{variant.Id}", AdminVariantResponse.From(variant));
    }

    private static async Task<Results<Ok<AdminVariantResponse>, ValidationProblem, NotFound, ProblemHttpResult>> UpdateVariantAsync(
        Guid productId,
        Guid variantId,
        VariantRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product?.Variants.SingleOrDefault(candidate => candidate.Id == variantId) is not { } variant)
        {
            return TypedResults.NotFound();
        }

        if (Refused(request, product) is { } errors)
        {
            return errors;
        }

        var sku = ProductVariant.Normalise(request.Sku!);

        if (sku != variant.Sku && await TakenAsync(dbContext, sku, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A product with this SKU already exists");
        }

        variant.Rename(sku);
        variant.UpdatePhysicalData(request.Ean, request.WeightGrams);
        variant.Choose(Cleaned(request.OptionValues));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminVariantResponse.From(variant));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveVariantAsync(
        Guid productId,
        Guid variantId,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null || product.Variants.All(variant => variant.Id != variantId))
        {
            return TypedResults.NotFound();
        }

        if (!product.RemoveVariant(variantId))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A product needs at least one form",
                detail: "Delete the product instead of its last variant.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static ValidationProblem? Refused(VariantRequest request, Product product)
    {
        var errors = new RequestErrors()
            .Check(
                !string.IsNullOrWhiteSpace(request.Sku) && request.Sku.Trim().Length <= ProductVariant.MaxSkuLength,
                "sku",
                $"SKU is required (up to {ProductVariant.MaxSkuLength} characters).")
            .Check(IsValidEan(request.Ean), "ean", "EAN must have 8 to 14 digits.")
            .Check(request.WeightGrams is null or >= 0, "weightGrams", "Weight cannot be negative.")
            .Check(
                Cleaned(request.OptionValues).Length == product.OptionNames.Length,
                "optionValues",
                $"This product needs one value for each of: {string.Join(", ", product.OptionNames)}.");

        return errors.Any ? errors.ToProblem() : null;
    }

    private static Task<bool> TakenAsync(DbContext dbContext, string sku, CancellationToken cancellationToken) =>
        dbContext.Set<ProductVariant>().AnyAsync(existing => existing.Sku == sku, cancellationToken);

    private static string[] Cleaned(IReadOnlyList<string>? values) =>
        [.. (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())];

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

internal sealed record VariantRequest(string? Sku, string? Ean, int? WeightGrams, IReadOnlyList<string>? OptionValues);

internal sealed record SetProductOptionsRequest(IReadOnlyList<string>? Names, IReadOnlyDictionary<Guid, IReadOnlyList<string>>? Values);

internal sealed record UpdateProductRequest(string? Ean, int? WeightGrams);

internal sealed record AdminProductResponse(
    Guid Id,
    string Sku,
    string? Ean,
    int? WeightGrams,
    IReadOnlyList<string> OptionNames,
    List<AdminVariantResponse> Variants,
    List<AdminImageResponse> Images)
{
    // Sku, Ean and WeightGrams are the first variant's, so a shop that sells one form of a thing reads the same
    // answer it always did.
    public static AdminProductResponse From(Product product) => new(
        product.Id,
        product.Default.Sku,
        product.Default.Ean,
        product.Default.WeightGrams,
        product.OptionNames,
        [.. product.Variants.OrderBy(variant => variant.Position).Select(AdminVariantResponse.From)],
        [.. product.Images.OrderBy(image => image.Position).Select(image => AdminImageResponse.From(product.Id, image))]);
}

internal sealed record AdminVariantResponse(Guid Id, string Sku, string? Ean, int? WeightGrams, IReadOnlyList<string> OptionValues, int Position)
{
    public static AdminVariantResponse From(ProductVariant variant) =>
        new(variant.Id, variant.Sku, variant.Ean, variant.WeightGrams, variant.OptionValues, variant.Position);
}

internal sealed record AdminImageResponse(Guid Id, string Url, string? AltText, int Position)
{
    public static AdminImageResponse From(Guid productId, ProductImage image) =>
        new(image.Id, $"/api/admin/products/{productId}/images/{image.Id}", image.AltText, image.Position);
}
