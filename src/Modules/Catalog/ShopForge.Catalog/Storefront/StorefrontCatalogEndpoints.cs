using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Attributes;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Http;
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

    private static async Task<Results<Ok<ProductPageResponse>, NotFound, ValidationProblem>> GetProductsAsync(
        HttpRequest request,
        DbContext dbContext,
        string? category,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 24, 1, MaxPageSize);
        Guid? categoryId = null;

        if (category is not null)
        {
            categoryId = await dbContext.Set<Category>()
                .Where(candidate => candidate.Slug == category)
                .Select(candidate => (Guid?)candidate.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (categoryId is null)
            {
                return TypedResults.NotFound();
            }
        }

        var definitions = await dbContext.Set<AttributeDefinition>().AsNoTracking().Include(definition => definition.Options).ToListAsync(cancellationToken);
        var errors = new RequestErrors();
        var filters = new List<ProductFilter>();

        foreach (var (key, value) in request.Query.Where(parameter => parameter.Key.StartsWith(ProductFilter.QueryPrefix, StringComparison.Ordinal)))
        {
            var code = key[ProductFilter.QueryPrefix.Length..];
            var definition = definitions.SingleOrDefault(definition => definition.Code == code && definition.IsFilterable);
            var filter = definition is null ? null : ProductFilter.Parse(definition, value.ToString());

            errors.Check(filter is not null, key, "Unknown filter or invalid filter value.");

            if (filter is not null)
            {
                filters.Add(filter);
            }
        }

        var productSort = ParseSort(sort, definitions);
        errors.Check(productSort is not null, "sort", "Use price, name or attr.<code>, optionally prefixed with '-'.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var query = new ProductQuery(dbContext, categoryId, filters);
        var products = query.Products();
        var totalCount = await products.CountAsync(cancellationToken);
        var items = await ProductQuery.Sort(products, productSort!)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(storeProduct => new
            {
                storeProduct.Id,
                storeProduct.Slug,
                storeProduct.Name,
                storeProduct.Price,
                ImageId = dbContext.Set<Product>()
                    .Where(product => product.Id == storeProduct.ProductId)
                    .SelectMany(product => product.Images)
                    .OrderBy(image => image.Position)
                    .Select(image => (Guid?)image.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var facets = new List<ProductFacetResponse>();

        foreach (var definition in await FacetDefinitionsAsync(dbContext, categoryId, definitions, cancellationToken))
        {
            facets.Add(await FacetAsync(query, definition, filters.SingleOrDefault(filter => filter.Definition == definition), cancellationToken));
        }

        return TypedResults.Ok(new ProductPageResponse(
            [.. items.Select(item => new ProductSummaryResponse(item.Id, item.Slug, item.Name, item.Price, ImageUrl(item.Id, item.ImageId)))],
            totalCount,
            pageNumber,
            size,
            facets));
    }

    private static async Task<Results<Ok<ProductDetailResponse>, NotFound>> GetProductAsync(
        string slug,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var storeProduct = await dbContext.Set<StoreProduct>()
            .AsNoTracking()
            .Include(storeProduct => storeProduct.Categories)
            .Include(storeProduct => storeProduct.AttributeValues)
            .AsSplitQuery()
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

        var definitionIds = storeProduct.AttributeValues.Select(value => value.AttributeDefinitionId).Distinct().ToList();
        var definitions = await dbContext.Set<AttributeDefinition>()
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.IsVisibleOnProductPage && definitionIds.Contains(definition.Id))
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ProductDetailResponse(
            storeProduct.Id,
            storeProduct.Slug,
            storeProduct.Name,
            storeProduct.Description,
            storeProduct.Price,
            [.. product.Images.OrderBy(image => image.Position).Select(image => new ProductImageResponse(ImageUrl(storeProduct.Id, image.Id)!, image.AltText))],
            categories,
            [.. definitions.Select(definition => new ProductAttributeResponse(
                definition.Code,
                definition.Name,
                definition.Type,
                definition.Unit,
                AttributeValueJson.Write(definition, [.. storeProduct.AttributeValues.Where(value => value.AttributeDefinitionId == definition.Id)], optionNames: true)))]));
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

    private static async Task<List<AttributeDefinition>> FacetDefinitionsAsync(
        DbContext dbContext,
        Guid? categoryId,
        List<AttributeDefinition> definitions,
        CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return [.. definitions.Where(definition => definition.IsFilterable).OrderBy(definition => definition.SortOrder).ThenBy(definition => definition.Name)];
        }

        var categoryAttributeIds = await dbContext.Set<CategoryAttribute>()
            .Where(assignment => assignment.CategoryId == categoryId)
            .OrderBy(assignment => assignment.SortOrder)
            .Select(assignment => assignment.AttributeDefinitionId)
            .ToListAsync(cancellationToken);

        return [.. categoryAttributeIds.Select(id => definitions.Single(definition => definition.Id == id)).Where(definition => definition.IsFilterable)];
    }

    private static async Task<ProductFacetResponse> FacetAsync(
        ProductQuery query,
        AttributeDefinition definition,
        ProductFilter? selected,
        CancellationToken cancellationToken)
    {
        var values = query.FacetValues(definition);
        var facet = new ProductFacetResponse(definition.Code, definition.Name, definition.Type, definition.Unit);

        switch (definition.Type)
        {
            case AttributeType.Select or AttributeType.MultiSelect:
                var counts = await values
                    .GroupBy(value => value.OptionId)
                    .Select(group => new { OptionId = group.Key, Count = group.Count() })
                    .ToListAsync(cancellationToken);
                return facet with
                {
                    Options =
                    [
                        .. definition.Options.OrderBy(option => option.SortOrder).Select(option => new FacetOptionResponse(
                            option.Code,
                            option.Name,
                            counts.SingleOrDefault(count => count.OptionId == option.Id)?.Count ?? 0,
                            selected?.OptionIds.Contains(option.Id) == true)),
                    ],
                };
            case AttributeType.Boolean:
                var booleans = await values
                    .GroupBy(value => value.BooleanValue)
                    .Select(group => new { Value = group.Key, Count = group.Count() })
                    .ToListAsync(cancellationToken);
                return facet with
                {
                    TrueCount = booleans.SingleOrDefault(count => count.Value == true)?.Count ?? 0,
                    FalseCount = booleans.SingleOrDefault(count => count.Value == false)?.Count ?? 0,
                    Selected = selected?.Boolean,
                };
            case AttributeType.Integer:
                return facet with
                {
                    Min = await values.MinAsync(value => value.IntegerValue, cancellationToken),
                    Max = await values.MaxAsync(value => value.IntegerValue, cancellationToken),
                    SelectedMin = selected?.SelectedMin,
                    SelectedMax = selected?.SelectedMax,
                };
            case AttributeType.Decimal:
                return facet with
                {
                    Min = await values.MinAsync(value => value.DecimalValue, cancellationToken),
                    Max = await values.MaxAsync(value => value.DecimalValue, cancellationToken),
                    SelectedMin = selected?.SelectedMin,
                    SelectedMax = selected?.SelectedMax,
                };
            default:
                return facet with
                {
                    Min = await values.MinAsync(value => value.DateValue, cancellationToken),
                    Max = await values.MaxAsync(value => value.DateValue, cancellationToken),
                    SelectedMin = selected?.SelectedMin,
                    SelectedMax = selected?.SelectedMax,
                };
        }
    }

    private static ProductSort? ParseSort(string? sort, List<AttributeDefinition> definitions)
    {
        if (string.IsNullOrEmpty(sort))
        {
            return new ProductSort("default", Descending: false, Attribute: null);
        }

        var descending = sort.StartsWith('-');
        var key = descending ? sort[1..] : sort;

        if (key is "price" or "name")
        {
            return new ProductSort(key, descending, Attribute: null);
        }

        const string attributePrefix = "attr.";
        var attribute = key.StartsWith(attributePrefix, StringComparison.Ordinal)
            ? definitions.SingleOrDefault(definition => definition.Code == key[attributePrefix.Length..])
            : null;

        return attribute is { Type: AttributeType.Text or AttributeType.Integer or AttributeType.Decimal or AttributeType.Date }
            ? new ProductSort("attribute", descending, attribute)
            : null;
    }

    private static string? ImageUrl(Guid storeProductId, Guid? imageId) =>
        imageId is null ? null : $"/api/storefront/products/{storeProductId}/images/{imageId}";
}

internal sealed record StorefrontCategoryResponse(string Name, string Slug);

internal sealed record ProductPageResponse(List<ProductSummaryResponse> Items, int TotalCount, int Page, int PageSize, List<ProductFacetResponse> Filters);

internal sealed record ProductSummaryResponse(Guid Id, string Slug, string Name, decimal Price, string? ImageUrl);

internal sealed record ProductFacetResponse(string Code, string Name, AttributeType Type, string? Unit)
{
    public List<FacetOptionResponse>? Options { get; init; }

    public object? Min { get; init; }

    public object? Max { get; init; }

    public object? SelectedMin { get; init; }

    public object? SelectedMax { get; init; }

    public int? TrueCount { get; init; }

    public int? FalseCount { get; init; }

    public bool? Selected { get; init; }
}

internal sealed record FacetOptionResponse(string Code, string Name, int Count, bool Selected);

internal sealed record ProductDetailResponse(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    decimal Price,
    List<ProductImageResponse> Images,
    List<StorefrontCategoryResponse> Categories,
    List<ProductAttributeResponse> Attributes);

internal sealed record ProductImageResponse(string Url, string? AltText);

internal sealed record ProductAttributeResponse(string Code, string Name, AttributeType Type, string? Unit, object? Value);
