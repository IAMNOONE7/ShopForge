using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Attributes;
using ShopForge.Catalog.Categories;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Images;
using ShopForge.Shared.Files;
using ShopForge.Shared.Http;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Stores;

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
        var categories = await StoreCategories.ReadAsync(dbContext, cancellationToken);

        // Flat, in tree order, each one naming its parent and carrying the path down to it. A menu can be
        // nested from that in one pass, and nothing that already read this list has to change to keep working.
        return TypedResults.Ok(categories.InTreeOrder().Select(row => Crumbed(categories, row)).ToList());
    }

    // A page whose address has changed is not missing; it has moved. This is an API resource, so the redirect
    // names the API's new address for it rather than the page's: whoever asked wanted this product's details,
    // and following the redirect gives them exactly that, with the new slug in the answer for the shop to put
    // in the address bar. The page-level redirect a crawler needs belongs to whatever serves the page (D-166).
    private static string ProductResource(string slug) => $"/api/storefront/products/{Uri.EscapeDataString(slug)}";

    private static string CategoryResource(string slug) => $"/api/storefront/products?category={Uri.EscapeDataString(slug)}";

    private static async Task<RedirectHttpResult?> MovedAsync(
        DbContext dbContext,
        SlugKind kind,
        string slug,
        Func<string, string> pageOf,
        CancellationToken cancellationToken)
    {
        var moved = await dbContext.Set<SlugHistory>()
            .AsNoTracking()
            .Where(history => history.Kind == kind && history.Slug == slug)
            .Select(history => history.PointsAt)
            .SingleOrDefaultAsync(cancellationToken);

        if (moved == Guid.Empty)
        {
            return null;
        }

        // Where that row is called now, which is the newest name after any number of renames.
        var nowCalled = kind == SlugKind.Listing
            ? await dbContext.Set<StoreProduct>().AsNoTracking()
                .Where(listing => listing.Id == moved && listing.IsVisible).Select(listing => listing.Slug).SingleOrDefaultAsync(cancellationToken)
            : await dbContext.Set<Category>().AsNoTracking()
                .Where(category => category.Id == moved).Select(category => category.Slug).SingleOrDefaultAsync(cancellationToken);

        return nowCalled is null ? null : TypedResults.Redirect(pageOf(nowCalled), permanent: true);
    }

    private static async Task<Results<Ok<ProductPageResponse>, RedirectHttpResult, NotFound, ValidationProblem>> GetProductsAsync(
        HttpRequest request,
        DbContext dbContext,
        IStockLedger stock,
        ICurrentStoreSettings storeSettings,
        string? category,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 24, 1, MaxPageSize);
        var settings = await storeSettings.GetAsync(cancellationToken);
        var page_ = PageMetadata.For(settings.Seo, settings.Name, PageSeoOverrides.None);
        string? pageText = null;
        List<Guid> categoryIds = [];
        List<StorefrontCategoryResponse> path = [];
        List<StorefrontCategoryResponse> children = [];

        if (category is not null)
        {
            // Every category of this shop, which is the one query the chosen one used to cost on its own and
            // now also answers what is above it, beneath it and directly under it (D-172).
            var categories = await StoreCategories.ReadAsync(dbContext, cancellationToken);
            var chosen = categories.Called(category);

            if (chosen is null)
            {
                return await MovedAsync(dbContext, SlugKind.Category, category, CategoryResource, cancellationToken) is { } moved
                    ? moved
                    : TypedResults.NotFound();
            }

            pageText = chosen.PageText;

            // The page sells what this category sells and what everything beneath it sells, each thing once
            // (D-146). Counts, facets, sorting and paging all read this same set.
            categoryIds = [.. categories.Shape.AndBeneath(chosen.Id)];
            path = [.. categories.PathTo(chosen.Id).Select(row => new StorefrontCategoryResponse(row.Name, row.Slug, null, []))];
            children = [.. categories.ChildrenOf(chosen.Id).Select(row => new StorefrontCategoryResponse(row.Name, row.Slug, chosen.Slug, []))];

            // A category has no image of its own, so a link shared from one shows whatever the shop offers.
            page_ = PageMetadata.For(settings.Seo, chosen.Name, new PageSeoOverrides(chosen.SeoTitle, chosen.SeoDescription, null, NoIndex: false));
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
        errors.Check(productSort is not null, "sort", "Use price, name, rating or attr.<code>, optionally prefixed with '-'.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var query = new ProductQuery(dbContext, categoryIds, filters);
        var products = query.Products();
        var totalCount = await products.CountAsync(cancellationToken);
        var items = await query.Sort(products, productSort!)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(storeProduct => new
            {
                storeProduct.Id,
                storeProduct.ProductId,
                storeProduct.Slug,
                storeProduct.Name,
                storeProduct.Price,
                storeProduct.RatingAverage,
                storeProduct.RatingCount,
                ImageId = dbContext.Set<Product>()
                    .Where(product => product.Id == storeProduct.ProductId)
                    .SelectMany(product => product.Images)
                    .OrderBy(image => image.Position)
                    .Select(image => (Guid?)image.Id)
                    .FirstOrDefault(),
                VariantIds = dbContext.Set<Product>()
                    .Where(product => product.Id == storeProduct.ProductId)
                    .SelectMany(product => product.Variants)
                    .Select(variant => variant.Id)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        // A card says what the shopper could buy, which is every form of it added up; the product page is where
        // one form is chosen (D-135).
        var available = await stock.AvailableAsync([.. items.SelectMany(item => item.VariantIds)], cancellationToken);
        var facets = new List<ProductFacetResponse>();

        foreach (var definition in await FacetDefinitionsAsync(dbContext, categoryIds, definitions, cancellationToken))
        {
            facets.Add(await FacetAsync(query, definition, filters.SingleOrDefault(filter => filter.Definition == definition), cancellationToken));
        }

        return TypedResults.Ok(new ProductPageResponse(
            [
                .. items.Select(item => new ProductSummaryResponse(
                    item.Id,
                    item.Slug,
                    item.Name,
                    item.Price,
                    item.VariantIds.Sum(available.GetValueOrDefault),
                    item.RatingAverage,
                    item.RatingCount,
                    ImageUrl(item.Id, item.ImageId))),
            ],
            totalCount,
            pageNumber,
            size,
            facets,
            page_,
            pageText,
            path,
            children,
            CategoryLinkedData(settings, path)));
    }

    private static async Task<Results<Ok<ProductDetailResponse>, RedirectHttpResult, NotFound>> GetProductAsync(
        string slug,
        DbContext dbContext,
        IStockLedger stock,
        ICurrentStoreSettings storeSettings,
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
            // Somebody wrote this address down before it changed. The trail says where that page went, and a
            // slug now belonging to a live listing never reaches this (D-166).
            return await MovedAsync(dbContext, SlugKind.Listing, slug, ProductResource, cancellationToken) is { } moved
                ? moved
                : TypedResults.NotFound();
        }

        var product = await dbContext.Set<Product>().AsNoTracking().SingleAsync(product => product.Id == storeProduct.ProductId, cancellationToken);
        // The same single query as before, widened to every category of the shop so each one the product is
        // in can carry the trail above it — which is what a breadcrumb on a product page is drawn from.
        var categoryIds = storeProduct.Categories.Select(assignment => assignment.CategoryId).ToHashSet();
        var tree = await StoreCategories.ReadAsync(dbContext, cancellationToken);
        var categories = tree.InTreeOrder()
            .Where(row => categoryIds.Contains(row.Id))
            .Select(row => Crumbed(tree, row))
            .ToList();

        var definitionIds = storeProduct.AttributeValues.Select(value => value.AttributeDefinitionId).Distinct().ToList();
        var definitions = await dbContext.Set<AttributeDefinition>()
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.IsVisibleOnProductPage && definitionIds.Contains(definition.Id))
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Name)
            .ToListAsync(cancellationToken);

        var variants = product.Variants.OrderBy(variant => variant.Position).ToList();
        var available = await stock.AvailableAsync([.. variants.Select(variant => variant.Id)], cancellationToken);

        var settings = await storeSettings.GetAsync(cancellationToken);
        var seo = PageMetadata.For(
            settings.Seo,
            storeProduct.Name,
            new PageSeoOverrides(storeProduct.SeoTitle, storeProduct.SeoDescription, storeProduct.SeoSocialImageUrl, storeProduct.SeoNoIndex));
        var images = product.Images.OrderBy(image => image.Position).Select(image => ImageUrl(storeProduct.Id, image.Id)!).ToList();

        return TypedResults.Ok(new ProductDetailResponse(
            storeProduct.Id,
            storeProduct.Slug,
            storeProduct.Name,
            storeProduct.Description,
            storeProduct.Price,
            variants.Sum(variant => available.GetValueOrDefault(variant.Id)),
            product.OptionNames,
            [.. variants.Select(variant => new ProductVariantResponse(
                variant.Id,
                variant.OptionValues,
                available.GetValueOrDefault(variant.Id)))],
            storeProduct.RatingAverage,
            storeProduct.RatingCount,
            [.. product.Images.OrderBy(image => image.Position).Select(image => new ProductImageResponse(ImageUrl(storeProduct.Id, image.Id)!, image.AltText))],
            categories,
            ProductLinkedData(settings, storeProduct.Slug, storeProduct.Name, storeProduct.Description, storeProduct.Price,
                variants.Sum(variant => available.GetValueOrDefault(variant.Id)), variants, images, product.Brand,
                storeProduct.RatingAverage, storeProduct.RatingCount, categories),
            [.. definitions.Select(definition => new ProductAttributeResponse(
                definition.Code,
                definition.Name,
                definition.Type,
                definition.Unit,
                AttributeValueJson.Write(definition, [.. storeProduct.AttributeValues.Where(value => value.AttributeDefinitionId == definition.Id)], optionNames: true)))],
            seo));
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

        var result = await ImageResults.StreamAsync(filePath, fileStorage, cancellationToken);

        // Image files never change: a new upload gets a new id and therefore a new URL. A 404 must not be cached
        // the same way, because the product may become visible later.
        if (result.Result is FileStreamHttpResult)
        {
            httpContext.Response.Headers.CacheControl = "public, max-age=86400, immutable";
        }

        return result;
    }

    // The filters on a page describe the products on that page. A parent sells what its children sell, so it
    // offers what they offer too: a shopper looking at four chairs under Furniture can narrow them by material
    // without first having to notice that Chairs is where the filter lives (D-172). Said once for a leaf,
    // which is every category until a merchant nests one.
    private static async Task<List<AttributeDefinition>> FacetDefinitionsAsync(
        DbContext dbContext,
        IReadOnlyList<Guid> categoryIds,
        List<AttributeDefinition> definitions,
        CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0)
        {
            return [.. definitions.Where(definition => definition.IsFilterable).OrderBy(definition => definition.SortOrder).ThenBy(definition => definition.Name)];
        }

        var assignments = await dbContext.Set<CategoryAttribute>()
            .Where(assignment => categoryIds.Contains(assignment.CategoryId))
            .Select(assignment => new { assignment.AttributeDefinitionId, assignment.SortOrder })
            .ToListAsync(cancellationToken);

        // Where one category in the set is in play this is exactly the order that category was given; where
        // several are, the earliest place an attribute was put decides, and the shop's own order breaks ties.
        return
        [
            .. assignments
                .GroupBy(assignment => assignment.AttributeDefinitionId)
                .Select(group => new { Definition = definitions.Single(definition => definition.Id == group.Key), Placed = group.Min(assignment => assignment.SortOrder) })
                .Where(facet => facet.Definition.IsFilterable)
                .OrderBy(facet => facet.Placed)
                .ThenBy(facet => facet.Definition.SortOrder)
                .ThenBy(facet => facet.Definition.Name)
                .Select(facet => facet.Definition),
        ];
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

        if (key is "price" or "name" or "rating")
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

    // What a product page declares itself to be: the thing for sale, and the trail down to it from the shop's
    // front page (D-173).
    private static List<object> ProductLinkedData(
        StoreSettings settings,
        string slug,
        string name,
        string? description,
        decimal price,
        int availableInAllForms,
        List<ProductVariant> variants,
        List<string> imagePaths,
        string? brand,
        decimal rating,
        int reviewCount,
        List<StorefrontCategoryResponse> categories)
    {
        if (settings.Address is not { } address)
        {
            return [];
        }

        var first = variants[0];

        // A barcode and a manufacturer's number name one particular thing. Where a listing is sold in several
        // forms the page describes the listing, so declaring one form's identifiers for all of them would be
        // a false statement about the others; the SKU of the first form stays, because something has to say
        // which row this is (D-163).
        var oneForm = variants.Count == 1;

        var documents = new List<object>
        {
            StructuredData.Product(address, new ProductFacts(
                name,
                description,
                slug,
                first.Sku,
                price,
                settings.Currency,
                availableInAllForms,
                variants.Count,
                [.. imagePaths.Select(address.Image)],
                brand,
                oneForm ? first.Ean : null,
                oneForm ? first.PartNumber : null,
                oneForm ? first.Condition?.ToString() : null,
                rating,
                reviewCount)),
        };

        // A thing in several categories has one trail to offer, and the shop's own order is the tie-break the
        // feed uses for the same question (D-172).
        if (categories.FirstOrDefault() is { } category)
        {
            documents.Add(StructuredData.Breadcrumbs(
                address,
                settings.Name,
                [
                    .. category.Path.Select(crumb => new Crumb(crumb.Name, address.Category(crumb.Slug))),
                    new Crumb(name, address.Product(slug)),
                ]));
        }

        return documents;
    }

    // What this page declares itself to be. A category page is a trail down to a list; the front page, which
    // is the same list with nothing chosen, is where the shop says who it is (D-173). A shop with no proved
    // domain declares nothing at all, because every address in these documents is absolute and there is none
    // to build (D-164).
    private static List<object> CategoryLinkedData(StoreSettings settings, List<StorefrontCategoryResponse> path)
    {
        if (settings.Address is not { } address)
        {
            return [];
        }

        if (path.Count == 0)
        {
            return
            [
                StructuredData.Organization(address, settings.Name, settings.Seller, settings.Branding.LogoUrl),
                StructuredData.WebSite(address, settings.Name),
            ];
        }

        return
        [
            StructuredData.Breadcrumbs(
                address,
                settings.Name,
                [.. path.Select(crumb => new Crumb(crumb.Name, address.Category(crumb.Slug)))]),
        ];
    }

    private static StorefrontCategoryResponse Crumbed(StoreCategoryTree categories, CategoryRow row) =>
        new(row.Name,
            row.Slug,
            row.ParentId is { } parentId ? categories.Row(parentId).Slug : null,
            [.. categories.PathTo(row.Id).Select(crumb => new StorefrontCategoryResponse(crumb.Name, crumb.Slug, null, []))]);

    private static string? ImageUrl(Guid storeProductId, Guid? imageId) =>
        imageId is null ? null : $"/api/storefront/products/{storeProductId}/images/{imageId}";
}

// A category says where it sits as well as what it is called: who it is under, and the trail from the top
// down to it so a breadcrumb can be drawn without asking again (D-172). The crumbs in a path carry no path of
// their own, which is what stops the shape repeating itself all the way down.
internal sealed record StorefrontCategoryResponse(
    string Name,
    string Slug,
    string? ParentSlug,
    List<StorefrontCategoryResponse> Path);

// A category page is this list filtered by one category, so this is where that page's metadata belongs; with
// no category it is the shop's own front page (D-165).
internal sealed record ProductPageResponse(
    List<ProductSummaryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    List<ProductFacetResponse> Filters,
    PageSeo Seo,
    string? PageText,
    List<StorefrontCategoryResponse> Path,
    List<StorefrontCategoryResponse> Children,
    List<object> JsonLd);

internal sealed record ProductSummaryResponse(
    Guid Id,
    string Slug,
    string Name,
    decimal Price,
    int Available,
    decimal Rating,
    int ReviewCount,
    string? ImageUrl);

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
    int Available,
    IReadOnlyList<string> OptionNames,
    List<ProductVariantResponse> Variants,
    decimal Rating,
    int ReviewCount,
    List<ProductImageResponse> Images,
    List<StorefrontCategoryResponse> Categories,
    List<object> JsonLd,
    List<ProductAttributeResponse> Attributes,
    PageSeo Seo);

// One form of the product, what it is called along each axis, and what is left of it.
internal sealed record ProductVariantResponse(Guid Id, IReadOnlyList<string> OptionValues, int Available);

internal sealed record ProductImageResponse(string Url, string? AltText);

internal sealed record ProductAttributeResponse(string Code, string Name, AttributeType Type, string? Unit, object? Value);
