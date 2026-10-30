using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Images;
using ShopForge.Catalog.Search;
using ShopForge.Shared.Auditing;
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
        products.MapGet("/unmeasured", GetUnmeasuredAsync);
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
        products.MapPut("/{productId:guid}/images", ReorderImagesAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        products.MapPut("/{productId:guid}/images/{imageId:guid}", DescribeImageAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
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
            .Check(IsValidEan(request.Ean), "ean", "A barcode must be a GTIN of 8, 12, 13 or 14 digits whose check digit agrees.")
            .Check(request.PartNumber is null || request.PartNumber.Trim().Length <= ProductVariant.MaxPartNumberLength, "partNumber", "A part number can be up to 70 characters.")
            .Check(IsValidCondition(request.Condition), "condition", "A condition is New, Refurbished or Used.")
            .Check(request.Brand is null || request.Brand.Trim().Length <= Product.MaxBrandLength, "brand", "A brand can be up to 70 characters.")
            .Check(request.PartNumber is null || request.PartNumber.Trim().Length <= ProductVariant.MaxPartNumberLength, "partNumber", "A part number can be up to 70 characters.")
            .Check(IsValidCondition(request.Condition), "condition", "A condition is New, Refurbished or Used.");

        if (Measured(errors, request.WeightGrams, request.LengthMm, request.WidthMm, request.HeightMm, stored: null).Any)
        {
            return errors.ToProblem();
        }

        if (await Plans.RefusedAsync(dbContext, limits, adding: 1, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var product = new Product(storeContext.TenantId!.Value, request.Sku!, request.Ean, request.WeightGrams);
        product.Rebrand(request.Brand);
        product.Default.UpdatePhysicalData(request.Ean, request.WeightGrams, request.PartNumber, ConditionFrom(request.Condition));
        Measure(product.Default, request.LengthMm, request.WidthMm, request.HeightMm, request.ShipsAlone);

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
        EveryStoreSearch search,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(IsValidEan(request.Ean), "ean", "A barcode must be a GTIN of 8, 12, 13 or 14 digits whose check digit agrees.")
            .Check(request.PartNumber is null || request.PartNumber.Trim().Length <= ProductVariant.MaxPartNumberLength, "partNumber", "A part number can be up to 70 characters.")
            .Check(IsValidCondition(request.Condition), "condition", "A condition is New, Refurbished or Used.")
            .Check(request.Brand is null || request.Brand.Trim().Length <= Product.MaxBrandLength, "brand", "A brand can be up to 70 characters.")
            .Check(request.PartNumber is null || request.PartNumber.Trim().Length <= ProductVariant.MaxPartNumberLength, "partNumber", "A part number can be up to 70 characters.")
            .Check(IsValidCondition(request.Condition), "condition", "A condition is New, Refurbished or Used.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null)
        {
            return TypedResults.NotFound();
        }

        // The brand belongs to the product whatever its shape, so it is settable here even for one sold in
        // several forms — which is the only place it could be set for such a product at all.
        product.Rebrand(request.Brand);

        // A barcode, a weight, a part number and a condition belong to a form, and a product with several has
        // no single answer. Nothing is saved on this path: the refusal is the whole response.
        if (product.Variants.Count > 1 && PerFormDataIn(request))
        {
            return new RequestErrors()
                .Check(false, "weightGrams", "This product is sold in several forms; set the barcode, the weight and the sizes on each one.")
                .ToProblem();
        }

        if (product.Variants.Count == 1)
        {
            if (Measured(new RequestErrors(), request.WeightGrams, request.LengthMm, request.WidthMm, request.HeightMm, product.Default) is { Any: true } unmeasured)
            {
                return unmeasured.ToProblem();
            }

            product.Default.UpdatePhysicalData(request.Ean, request.WeightGrams, request.PartNumber, ConditionFrom(request.Condition));
            Measure(product.Default, request.LengthMm, request.WidthMm, request.HeightMm, request.ShipsAlone);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // The brand, the barcode and the part number are words the product is found by in every store that sells it.
        await search.RefreshAsync([product.Id], besides: null, cancellationToken);

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
        EveryStoreSearch search,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null)
        {
            return TypedResults.NotFound();
        }

        if (Refused(request, product, stored: null) is { } errors)
        {
            return errors;
        }

        if (await TakenAsync(dbContext, ProductVariant.Normalise(request.Sku!), cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A product with this SKU already exists");
        }

        var variant = product.AddVariant(request.Sku!, request.Ean, request.WeightGrams, Cleaned(request.OptionValues));
        variant.UpdatePhysicalData(request.Ean, request.WeightGrams, request.PartNumber, ConditionFrom(request.Condition));
        Measure(variant, request.LengthMm, request.WidthMm, request.HeightMm, request.ShipsAlone);
        await dbContext.SaveChangesAsync(cancellationToken);
        await search.RefreshAsync([product.Id], besides: null, cancellationToken);

        return TypedResults.Created($"/api/admin/products/{productId}/variants/{variant.Id}", AdminVariantResponse.From(variant));
    }

    private static async Task<Results<Ok<AdminVariantResponse>, ValidationProblem, NotFound, ProblemHttpResult>> UpdateVariantAsync(
        Guid productId,
        Guid variantId,
        VariantRequest request,
        DbContext dbContext,
        EveryStoreSearch search,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product?.Variants.SingleOrDefault(candidate => candidate.Id == variantId) is not { } variant)
        {
            return TypedResults.NotFound();
        }

        if (Refused(request, product, variant) is { } errors)
        {
            return errors;
        }

        var sku = ProductVariant.Normalise(request.Sku!);

        if (sku != variant.Sku && await TakenAsync(dbContext, sku, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A product with this SKU already exists");
        }

        variant.Rename(sku);
        variant.UpdatePhysicalData(request.Ean, request.WeightGrams, request.PartNumber, ConditionFrom(request.Condition));
        Measure(variant, request.LengthMm, request.WidthMm, request.HeightMm, request.ShipsAlone);
        variant.Choose(Cleaned(request.OptionValues));
        await dbContext.SaveChangesAsync(cancellationToken);

        // A renamed SKU is findable at once in every store that sells the product (I10, D-226).
        await search.RefreshAsync([product.Id], besides: null, cancellationToken);

        return TypedResults.Ok(AdminVariantResponse.From(variant));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveVariantAsync(
        Guid productId,
        Guid variantId,
        DbContext dbContext,
        EveryStoreSearch search,
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
        await search.RefreshAsync([productId], besides: null, cancellationToken);

        return TypedResults.NoContent();
    }

    private static ValidationProblem? Refused(VariantRequest request, Product product, ProductVariant? stored)
    {
        var errors = new RequestErrors()
            .Check(
                !string.IsNullOrWhiteSpace(request.Sku) && request.Sku.Trim().Length <= ProductVariant.MaxSkuLength,
                "sku",
                $"SKU is required (up to {ProductVariant.MaxSkuLength} characters).")
            .Check(IsValidEan(request.Ean), "ean", "A barcode must be a GTIN of 8, 12, 13 or 14 digits whose check digit agrees.")
            .Check(request.PartNumber is null || request.PartNumber.Trim().Length <= ProductVariant.MaxPartNumberLength, "partNumber", "A part number can be up to 70 characters.")
            .Check(IsValidCondition(request.Condition), "condition", "A condition is New, Refurbished or Used.")
            .Check(
                Cleaned(request.OptionValues).Length == product.OptionNames.Length,
                "optionValues",
                $"This product needs one value for each of: {string.Join(", ", product.OptionNames)}.");

        return Measured(errors, request.WeightGrams, request.LengthMm, request.WidthMm, request.HeightMm, stored).Any ? errors.ToProblem() : null;
    }

    // The forms that cannot yet be priced for delivery, so they can be found and finished before a shopper meets one:
    // no weight, a weight of nothing, or no sizes (D-214).
    private static async Task<Ok<AdminUnmeasuredResponse>> GetUnmeasuredAsync(
        DbContext dbContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var unmeasured = dbContext.Set<ProductVariant>().AsNoTracking().Where(ProductVariant.Unmeasured);
        var total = await unmeasured.CountAsync(cancellationToken);
        var variants = await unmeasured
            .OrderBy(variant => variant.Sku)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(variant => new { variant.ProductId, variant.Id, variant.Sku, variant.OptionValues, variant.WeightGrams, variant.LengthMm })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AdminUnmeasuredResponse(
            total,
            [
                .. variants.Select(variant => new AdminUnmeasuredVariant(
                    variant.ProductId,
                    variant.Id,
                    variant.Sku,
                    variant.OptionValues,
                    [
                        .. variant.WeightGrams is > 0 ? Array.Empty<string>() : ["weightGrams"],
                        .. variant.LengthMm is null ? ["lengthMm", "widthMm", "heightMm"] : Array.Empty<string>(),
                    ])),
            ]));
    }

    // Every save leaves a form weighed and measured: a weight of at least a gram, as a save replaces it, and the
    // length, the width and the height, which come together or not at all. Sizes a request leaves out keep what is
    // stored, so a save about something else need not repeat them (D-216).
    private static RequestErrors Measured(RequestErrors errors, int? weightGrams, int? lengthMm, int? widthMm, int? heightMm, ProductVariant? stored)
    {
        const string Rule = "A size is a whole number of millimetres from 1 to 10000.";
        var given = new[] { lengthMm, widthMm, heightMm }.Count(size => size is not null);

        return errors
            .Check(weightGrams is > 0, "weightGrams", "Every product needs a weight of at least 1 gram.")
            .Check(given > 0 || stored?.LengthMm is not null, "lengthMm", "Every product needs its length, width and height in millimetres.")
            .Check(
                given is 0 or 3,
                lengthMm is null ? "lengthMm" : widthMm is null ? "widthMm" : "heightMm",
                "The length, the width and the height go together: give all three, in millimetres.")
            .Check(lengthMm is null || ProductVariant.IsSize(lengthMm.Value), "lengthMm", Rule)
            .Check(widthMm is null || ProductVariant.IsSize(widthMm.Value), "widthMm", Rule)
            .Check(heightMm is null || ProductVariant.IsSize(heightMm.Value), "heightMm", Rule);
    }

    private static void Measure(ProductVariant variant, int? lengthMm, int? widthMm, int? heightMm, bool? shipsAlone)
    {
        if (lengthMm is { } length && widthMm is { } width && heightMm is { } height)
        {
            variant.Measure(length, width, height);
        }

        if (shipsAlone is { } alone)
        {
            variant.SendAlone(alone);
        }
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


    // The order they are shown in, which also decides the thumbnail: the first picture is the one a card and
    // a shopping feed carry (D-182).
    private static async Task<Results<Ok<List<AdminImageResponse>>, NotFound>> ReorderImagesAsync(
        Guid productId,
        ReorderImagesRequest request,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product is null)
        {
            return TypedResults.NotFound();
        }

        product.ReorderImages(request.ImageIds ?? []);
        audit.Record("catalog.product.images.reordered", productId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(Described(productId, product));
    }

    // Saying what the picture shows. Nobody writes it at upload and everybody wants to afterwards, which is
    // the whole reason this exists separately.
    private static async Task<Results<Ok<AdminImageResponse>, ValidationProblem, NotFound>> DescribeImageAsync(
        Guid productId,
        Guid imageId,
        DescribeImageRequest request,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (product?.Image(imageId) is not { } image)
        {
            return TypedResults.NotFound();
        }

        var errors = new RequestErrors()
            .Check((request.AltText ?? string.Empty).Trim().Length <= 200, "altText", "A description can be up to 200 characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        image.Describe(request.AltText);
        audit.Record("catalog.product.image.described", productId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminImageResponse.From(productId, image));
    }

    private static List<AdminImageResponse> Described(Guid productId, Product product) =>
        [.. product.Images.OrderBy(image => image.Position).Select(image => AdminImageResponse.From(productId, image))];

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

    private static bool IsValidEan(string? ean) => string.IsNullOrWhiteSpace(ean) || Gtin.IsValid(ean);

    private static bool PerFormDataIn(UpdateProductRequest request) =>
        request.Ean is not null || request.WeightGrams is not null || request.PartNumber is not null || request.Condition is not null
        || request.LengthMm is not null || request.WidthMm is not null || request.HeightMm is not null || request.ShipsAlone is not null;

    private static bool IsValidCondition(string? condition) =>
        condition is null || Enum.TryParse<ProductCondition>(condition, ignoreCase: true, out _);

    private static ProductCondition? ConditionFrom(string? condition) =>
        string.IsNullOrWhiteSpace(condition) ? null : Enum.Parse<ProductCondition>(condition, ignoreCase: true);
}

internal sealed record CreateProductRequest(
    string? Sku,
    string? Ean,
    int? WeightGrams,
    string? Brand = null,
    string? PartNumber = null,
    string? Condition = null,
    int? LengthMm = null,
    int? WidthMm = null,
    int? HeightMm = null,
    bool? ShipsAlone = null);

internal sealed record VariantRequest(
    string? Sku,
    string? Ean,
    int? WeightGrams,
    IReadOnlyList<string>? OptionValues,
    string? PartNumber = null,
    string? Condition = null,
    int? LengthMm = null,
    int? WidthMm = null,
    int? HeightMm = null,
    bool? ShipsAlone = null);

internal sealed record SetProductOptionsRequest(IReadOnlyList<string>? Names, IReadOnlyDictionary<Guid, IReadOnlyList<string>>? Values);

internal sealed record UpdateProductRequest(
    string? Ean,
    int? WeightGrams,
    string? Brand = null,
    string? PartNumber = null,
    string? Condition = null,
    int? LengthMm = null,
    int? WidthMm = null,
    int? HeightMm = null,
    bool? ShipsAlone = null);

internal sealed record AdminProductResponse(
    Guid Id,
    string Sku,
    string? Ean,
    int? WeightGrams,
    int? LengthMm,
    int? WidthMm,
    int? HeightMm,
    bool ShipsAlone,
    string? Brand,
    IReadOnlyList<string> OptionNames,
    List<AdminVariantResponse> Variants,
    List<AdminImageResponse> Images)
{
    // Sku, Ean, the weight and the sizes are the first variant's, so a shop that sells one form of a thing reads the
    // same answer it always did.
    public static AdminProductResponse From(Product product) => new(
        product.Id,
        product.Default.Sku,
        product.Default.Ean,
        product.Default.WeightGrams,
        product.Default.LengthMm,
        product.Default.WidthMm,
        product.Default.HeightMm,
        product.Default.ShipsAlone,
        product.Brand,
        product.OptionNames,
        [.. product.Variants.OrderBy(variant => variant.Position).Select(AdminVariantResponse.From)],
        [.. product.Images.OrderBy(image => image.Position).Select(image => AdminImageResponse.From(product.Id, image))]);
}

internal sealed record AdminVariantResponse(
    Guid Id,
    string Sku,
    string? Ean,
    int? WeightGrams,
    int? LengthMm,
    int? WidthMm,
    int? HeightMm,
    bool ShipsAlone,
    string? PartNumber,
    string? Condition,
    IReadOnlyList<string> OptionValues,
    int Position)
{
    public static AdminVariantResponse From(ProductVariant variant) => new(
        variant.Id,
        variant.Sku,
        variant.Ean,
        variant.WeightGrams,
        variant.LengthMm,
        variant.WidthMm,
        variant.HeightMm,
        variant.ShipsAlone,
        variant.PartNumber,
        variant.Condition?.ToString(),
        variant.OptionValues,
        variant.Position);
}

// What is missing from each form: the API's own names for the fields, `weightGrams` and the three sizes.
internal sealed record AdminUnmeasuredResponse(int Total, IReadOnlyList<AdminUnmeasuredVariant> Variants);

internal sealed record AdminUnmeasuredVariant(Guid ProductId, Guid VariantId, string Sku, IReadOnlyList<string> OptionValues, IReadOnlyList<string> Missing);

internal sealed record ReorderImagesRequest(List<Guid>? ImageIds);

internal sealed record DescribeImageRequest(string? AltText);

internal sealed record AdminImageResponse(Guid Id, string Url, string? AltText, int Position)
{
    public static AdminImageResponse From(Guid productId, ProductImage image) =>
        new(image.Id, $"/api/admin/products/{productId}/images/{image.Id}", image.AltText, image.Position);
}
