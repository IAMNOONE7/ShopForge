using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Categories;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Publishing;
using ShopForge.Catalog.Retiring;
using ShopForge.Catalog.Search;
using ShopForge.Shared.Admin;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Admin;

internal static class AdminStoreCatalogEndpoints
{
    public static void MapAdminStoreCatalog(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/products", GetStoreProductsAsync);
        storeAdmin.MapPost("/products", ListProductAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPut("/products/{storeProductId:guid}", UpdateStoreProductAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPut("/products/{storeProductId:guid}/categories", AssignCategoriesAsync).RequireAuthorization(AdminPolicies.CatalogManagement);

        storeAdmin.MapGet("/categories", GetCategoriesAsync);
        storeAdmin.MapPost("/categories", CreateCategoryAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPut("/categories/{categoryId:guid}", UpdateCategoryAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPost("/products/{storeProductId:guid}/archive", ArchiveListingAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPost("/products/{storeProductId:guid}/restore", RestoreListingAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapDelete("/products/{storeProductId:guid}", DeleteListingAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPost("/categories/{categoryId:guid}/archive", ArchiveCategoryAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPost("/categories/{categoryId:guid}/restore", RestoreCategoryAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapDelete("/categories/{categoryId:guid}", DeleteCategoryAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
    }

    // A thousand listings is a list to work through a page at a time, and to look through by name or code
    // (D-179). The shopper's own search is a different question with its own index; this is the merchant
    // looking for a row they already know exists.
    //
    // The narrowing and the ordering are done on the rows, before they become responses: a projection that
    // carries a subquery and a collection cannot also be filtered.
    private static async Task<Ok<AdminListResponse<AdminStoreProductResponse>>> GetStoreProductsAsync(
        DbContext dbContext,
        int? page,
        int? pageSize,
        string? sort,
        string? q,
        bool? archived,
        CancellationToken cancellationToken)
    {
        var listed = AdminListQuery.Of(page, pageSize, sort, q);

        // The merchant's list is what they are working with. Asking for the retired ones is a different
        // screen, not a longer one, so it is a different list rather than a flag mixed into this one (D-180).
        var listings = archived == true
            ? dbContext.Set<StoreProduct>().AsNoTracking().IgnoreQueryFilters([TenancyFilters.Archived])
                .Where(listing => listing.ArchivedAt != null)
            : dbContext.Set<StoreProduct>().AsNoTracking();

        if (listed.Pattern is { } pattern)
        {
            listings = listings.Where(listing =>
                EF.Functions.Like(listing.Name.ToLower(), pattern)
                || EF.Functions.Like(listing.Slug.ToLower(), pattern)
                || dbContext.Set<Product>()
                    .Where(product => product.Id == listing.ProductId)
                    .SelectMany(product => product.Variants)
                    .Any(variant => variant.Sku != null && EF.Functions.Like(variant.Sku.ToLower(), pattern)));
        }

        listings = (listed.SortKey, listed.Descending) switch
        {
            ("name", false) => listings.OrderBy(listing => listing.Name).ThenBy(listing => listing.Id),
            ("name", true) => listings.OrderByDescending(listing => listing.Name).ThenBy(listing => listing.Id),
            ("price", false) => listings.OrderBy(listing => listing.Price).ThenBy(listing => listing.Id),
            ("price", true) => listings.OrderByDescending(listing => listing.Price).ThenBy(listing => listing.Id),

            // The shop's own order, which is what this screen is for arranging.
            _ => listings.OrderBy(listing => listing.SortOrder).ThenBy(listing => listing.Name).ThenBy(listing => listing.Id),
        };

        var total = await listings.CountAsync(cancellationToken);
        var wanted = await listings
            .Skip(listed.Skipped)
            .Take(listed.Taken)
            .Join(dbContext.Set<Product>(), listing => listing.ProductId, product => product.Id, (listing, product) => new AdminStoreProductResponse(
                listing.Id,
                product.Id,
                product.Variants.OrderBy(variant => variant.Position).First().Sku,
                listing.Name,
                listing.Slug,
                listing.Description,
                listing.Price,
                listing.VatRate,
                listing.IsVisible,
                listing.SortOrder,
                listing.Categories.Select(assignment => assignment.CategoryId).ToList(),
                listing.SeoTitle,
                listing.SeoDescription,
                listing.SeoSocialImageUrl,
                listing.SeoNoIndex))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AdminListResponse<AdminStoreProductResponse>(wanted, total, listed.Page, listed.PageSize));
    }

    private static async Task<Results<Created<AdminStoreProductResponse>, ValidationProblem, ProblemHttpResult>> ListProductAsync(
        ListProductRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        SearchIndex search,
        ICurrentStoreSettings storeSettings,
        CancellationToken cancellationToken)
    {
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var errors = ValidateDetails(request.Name, request.Slug, request.Price, request.VatRate, currency);

        // The tenant filter makes products of other tenants invisible, so they cannot be listed here.
        var product = await dbContext.Set<Product>().SingleOrDefaultAsync(product => product.Id == request.ProductId, cancellationToken);
        errors.Check(product is not null, "productId", "Product not found.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var details = ToDetails(request.Name!, request.Slug, request.Description, request.Price, request.VatRate, request.IsVisible, request.SortOrder);

        if (await dbContext.Set<StoreProduct>().AnyAsync(existing => existing.ProductId == product!.Id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The product is already listed in this store");
        }

        if (await dbContext.Set<StoreProduct>().AnyAsync(existing => existing.Slug == details.Slug, cancellationToken))
        {
            return SlugTaken();
        }

        var storeProduct = new StoreProduct(storeContext.StoreId!.Value, product!, details, currency);
        dbContext.Add(storeProduct);
        await dbContext.SaveChangesAsync(cancellationToken);
        await search.RefreshAsync(storeProduct.Id, cancellationToken);

        return TypedResults.Created(
            $"/api/admin/stores/{storeProduct.StoreId}/products/{storeProduct.Id}",
            AdminStoreProductResponse.From(storeProduct, product!));
    }

    private static async Task<Results<Ok<AdminStoreProductResponse>, ValidationProblem, ProblemHttpResult, NotFound>> UpdateStoreProductAsync(
        Guid storeProductId,
        UpdateStoreProductRequest request,
        DbContext dbContext,
        TimeProvider clock,
        IAuditLog audit,
        SearchIndex search,
        ICurrentStoreSettings storeSettings,
        CancellationToken cancellationToken)
    {
        var storeProduct = await dbContext.Set<StoreProduct>()
            .Include(storeProduct => storeProduct.Categories)
            .SingleOrDefaultAsync(storeProduct => storeProduct.Id == storeProductId, cancellationToken);

        if (storeProduct is null)
        {
            return TypedResults.NotFound();
        }

        // Without an explicit slug the public URL stays as it is, even when the name changes.
        var slug = request.Slug ?? storeProduct.Slug;
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var errors = ValidateDetails(request.Name, slug, request.Price, request.VatRate, currency);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var details = ToDetails(request.Name!, slug, request.Description, request.Price, request.VatRate, request.IsVisible, request.SortOrder);

        if (await dbContext.Set<StoreProduct>().AnyAsync(other => other.Slug == details.Slug && other.Id != storeProductId, cancellationToken))
        {
            return SlugTaken();
        }

        var wasPriced = storeProduct.Price;
        var wasCalled = storeProduct.Slug;
        storeProduct.Update(details, currency);
        await SlugTrail.RecordAsync(
            dbContext,
            storeProduct.StoreId,
            SlugKind.Listing,
            wasCalled,
            storeProduct.Slug,
            storeProduct.Id,
            clock.GetUtcNow(),
            cancellationToken);

        if (request.Seo is { } seo)
        {
            storeProduct.DescribeToSearchEngines(seo.Title, seo.Description, seo.SocialImageUrl, seo.NoIndex);
        }

        // Only the price is worth a line of its own: the rest of an edit is visible in the listing itself, while
        // what something used to cost is not (D-116).
        if (storeProduct.Price != wasPriced)
        {
            audit.Record("listing.price-changed", storeProduct.Name, new { from = wasPriced, to = storeProduct.Price });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // A name or a description that has changed is a different answer to what somebody types.
        await search.RefreshAsync(storeProduct.Id, cancellationToken);

        var product = await dbContext.Set<Product>().AsNoTracking().SingleAsync(product => product.Id == storeProduct.ProductId, cancellationToken);

        return TypedResults.Ok(AdminStoreProductResponse.From(storeProduct, product));
    }

    private static async Task<Results<Ok<AdminStoreProductResponse>, ValidationProblem, NotFound>> AssignCategoriesAsync(
        Guid storeProductId,
        AssignCategoriesRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var storeProduct = await dbContext.Set<StoreProduct>()
            .Include(storeProduct => storeProduct.Categories)
            .SingleOrDefaultAsync(storeProduct => storeProduct.Id == storeProductId, cancellationToken);

        if (storeProduct is null)
        {
            return TypedResults.NotFound();
        }

        var categoryIds = (request.CategoryIds ?? []).Distinct().ToList();
        var categories = await dbContext.Set<Category>().Where(category => categoryIds.Contains(category.Id)).ToListAsync(cancellationToken);

        var errors = new RequestErrors().Check(categories.Count == categoryIds.Count, "categoryIds", "One or more categories were not found.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        storeProduct.AssignCategories(categories);
        await dbContext.SaveChangesAsync(cancellationToken);

        var product = await dbContext.Set<Product>().AsNoTracking().SingleAsync(product => product.Id == storeProduct.ProductId, cancellationToken);

        return TypedResults.Ok(AdminStoreProductResponse.From(storeProduct, product));
    }

    private static async Task<Ok<List<AdminCategoryResponse>>> GetCategoriesAsync(
        DbContext dbContext,
        bool? archived,
        CancellationToken cancellationToken)
    {
        var categories = await (archived == true
                ? dbContext.Set<Category>().IgnoreQueryFilters([TenancyFilters.Archived]).Where(category => category.ArchivedAt != null)
                : dbContext.Set<Category>())
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new AdminCategoryResponse(
                category.Id,
                category.Name,
                category.Slug,
                category.SortOrder,
                category.ParentId,
                category.Attributes.OrderBy(assignment => assignment.SortOrder).Select(assignment => assignment.AttributeDefinitionId).ToList(),
                category.SeoTitle,
                category.SeoDescription,
                category.PageText))
            .ToListAsync(cancellationToken);

        // Parents before their children, so the editor can indent the list as it arrives.
        var shape = CategoryTree.Of([.. categories.Select(category => new CategoryPlace(category.Id, category.ParentId))]);
        var order = shape.Everything().Select((id, position) => (id, position)).ToDictionary(entry => entry.id, entry => entry.position);

        return TypedResults.Ok(categories.OrderBy(category => order[category.Id]).ToList());
    }

    private static async Task<Results<Created<AdminCategoryResponse>, ValidationProblem, ProblemHttpResult>> CreateCategoryAsync(
        CategoryRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateCategory(request);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var slug = request.Slug ?? Slugs.Create(request.Name!);

        if (await dbContext.Set<Category>().AnyAsync(existing => existing.Slug == slug, cancellationToken))
        {
            return SlugTaken();
        }

        if (request.ParentId is { } wantedParent)
        {
            var shape = await StoreCategories.ShapeAsync(dbContext, cancellationToken);

            if (!shape.Knows(wantedParent))
            {
                return NoSuchParent();
            }

            if (!shape.CanHoldAChild(wantedParent))
            {
                return TooDeep();
            }
        }

        var category = new Category(storeContext.StoreId!.Value, request.Name!, slug, request.SortOrder);
        category.MoveTo(request.ParentId);

        if (request.Seo is { } seo)
        {
            category.DescribeToSearchEngines(seo.Title, seo.Description, seo.PageText);
        }

        dbContext.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/admin/stores/{category.StoreId}/categories/{category.Id}",
            new AdminCategoryResponse(
                category.Id,
                category.Name,
                category.Slug,
                category.SortOrder,
                category.ParentId,
                [],
                category.SeoTitle,
                category.SeoDescription,
                category.PageText));
    }

    private static async Task<Results<Ok<AdminCategoryResponse>, ValidationProblem, ProblemHttpResult, NotFound>> UpdateCategoryAsync(
        Guid categoryId,
        CategoryRequest request,
        DbContext dbContext,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Set<Category>()
            .Include(category => category.Attributes)
            .SingleOrDefaultAsync(category => category.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return TypedResults.NotFound();
        }

        var slug = request.Slug ?? category.Slug;
        var errors = ValidateCategory(request with { Slug = slug });

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<Category>().AnyAsync(other => other.Slug == slug && other.Id != categoryId, cancellationToken))
        {
            return SlugTaken();
        }

        if (request.ParentId != category.ParentId)
        {
            var shape = await StoreCategories.ShapeAsync(dbContext, cancellationToken);

            if (request.ParentId is { } wantedParent && !shape.Knows(wantedParent))
            {
                return NoSuchParent();
            }

            // The tree says whether this move is allowed, because neither end of it can tell on its own: a
            // category cannot go beneath something it contains, and a shallow branch moved under a deep one
            // can break the depth limit while neither of them breaks it alone (D-172).
            if (!shape.CanAdopt(category.Id, request.ParentId))
            {
                if (request.ParentId == category.Id)
                {
                    return ItsOwnParent();
                }

                return shape.AndBeneath(category.Id).Contains(request.ParentId!.Value) ? WouldBeItsOwnAncestor() : TooDeep();
            }

            category.MoveTo(request.ParentId);
        }

        var wasCalled = category.Slug;
        category.Update(request.Name!, slug, request.SortOrder);
        await SlugTrail.RecordAsync(
            dbContext,
            category.StoreId,
            SlugKind.Category,
            wasCalled,
            category.Slug,
            category.Id,
            clock.GetUtcNow(),
            cancellationToken);

        if (request.Seo is { } seo)
        {
            category.DescribeToSearchEngines(seo.Title, seo.Description, seo.PageText);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AdminCategoryResponse(
            category.Id,
            category.Name,
            category.Slug,
            category.SortOrder,
            category.ParentId,
            [.. category.Attributes.OrderBy(assignment => assignment.SortOrder).Select(assignment => assignment.AttributeDefinitionId)],
            category.SeoTitle,
            category.SeoDescription,
            category.PageText));
    }


    // Retiring a listing: off the shop, out of the feeds and the sitemap, out of what a shopper can search,
    // and out of the merchant's own list unless they ask for it (D-180).
    private static async Task<Results<NoContent, NotFound>> ArchiveListingAsync(
        Guid storeProductId,
        DbContext dbContext,
        TimeProvider clock,
        IAuditLog audit,
        SearchIndex search,
        CancellationToken cancellationToken)
    {
        var listing = await dbContext.Set<StoreProduct>()
            .IgnoreQueryFilters([TenancyFilters.Archived])
            .SingleOrDefaultAsync(candidate => candidate.Id == storeProductId, cancellationToken);

        if (listing is null)
        {
            return TypedResults.NotFound();
        }

        if (listing.Archive(clock.GetUtcNow()))
        {
            audit.Record("catalog.listing.archived", listing.Slug);
            await dbContext.SaveChangesAsync(cancellationToken);

            // An archived listing is not a thing a shopper can find, so its words come out of the index.
            await search.RefreshAsync(listing.Id, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RestoreListingAsync(
        Guid storeProductId,
        DbContext dbContext,
        IAuditLog audit,
        SearchIndex search,
        CancellationToken cancellationToken)
    {
        var listing = await dbContext.Set<StoreProduct>()
            .IgnoreQueryFilters([TenancyFilters.Archived])
            .SingleOrDefaultAsync(candidate => candidate.Id == storeProductId, cancellationToken);

        if (listing is null)
        {
            return TypedResults.NotFound();
        }

        if (listing.Restore())
        {
            audit.Record("catalog.listing.restored", listing.Slug);
            await dbContext.SaveChangesAsync(cancellationToken);
            await search.RefreshAsync(listing.Id, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    // Deleting it properly, when nothing points at it. When something does, the answer says what — a merchant
    // told only "no" has nowhere to go, and archiving is the thing they actually wanted (D-180).
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteListingAsync(
        Guid storeProductId,
        DbContext dbContext,
        WhatPointsAtIt holding,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var listing = await dbContext.Set<StoreProduct>()
            .IgnoreQueryFilters([TenancyFilters.Archived])
            .SingleOrDefaultAsync(candidate => candidate.Id == storeProductId, cancellationToken);

        if (listing is null)
        {
            return TypedResults.NotFound();
        }

        if (await holding.HoldingAListingAsync(storeProductId, cancellationToken) is { } reason)
        {
            return StillInUse("listing", reason);
        }

        dbContext.Remove(listing);
        audit.Record("catalog.listing.deleted", listing.Slug);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> ArchiveCategoryAsync(
        Guid categoryId,
        DbContext dbContext,
        TimeProvider clock,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var category = await Categories(dbContext).SingleOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return TypedResults.NotFound();
        }

        if (category.Archive(clock.GetUtcNow()))
        {
            audit.Record("catalog.category.archived", category.Slug);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RestoreCategoryAsync(
        Guid categoryId,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var category = await Categories(dbContext).SingleOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return TypedResults.NotFound();
        }

        if (category.Restore())
        {
            audit.Record("catalog.category.restored", category.Slug);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteCategoryAsync(
        Guid categoryId,
        DbContext dbContext,
        WhatPointsAtIt holding,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var category = await Categories(dbContext).SingleOrDefaultAsync(candidate => candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return TypedResults.NotFound();
        }

        if (await holding.HoldingACategoryAsync(categoryId, cancellationToken) is { } reason)
        {
            return StillInUse("category", reason);
        }

        dbContext.Remove(category);
        audit.Record("catalog.category.deleted", category.Slug);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static IQueryable<Category> Categories(DbContext dbContext) =>
        dbContext.Set<Category>().IgnoreQueryFilters([TenancyFilters.Archived]);

    // The reason is the whole point: it names what is holding the row, so a merchant knows whether to empty
    // it, reassign it, or archive it instead.
    private static ProblemHttpResult StillInUse(string what, string reason) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: $"This {what} cannot be deleted",
            detail: $"It cannot be deleted because {reason}. Archive it instead to take it out of use and keep it.");

    private static RequestErrors ValidateDetails(string? name, string? slug, decimal price, decimal vatRate, Currency currency) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200, "name", "Name is required (up to 200 characters).")
            .Check(slug is null || Slugs.IsValid(slug), "slug", "Slug may contain lower-case letters, digits and single hyphens.")
            .Check(name is null || slug is not null || Slugs.Create(name).Length > 0, "slug", "A slug cannot be derived from this name; provide one.")
            .Check(price >= 0 && currency.Holds(price), "price", $"Price must be zero or more, with at most {currency.Decimals} decimals.")
            .Check(vatRate is >= 0 and <= 100 && decimal.Round(vatRate, 2) == vatRate, "vatRate", "The VAT rate must be between 0 and 100.");

    private static RequestErrors ValidateCategory(CategoryRequest request) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 200, "name", "Name is required (up to 200 characters).")
            .Check(request.Slug is null || Slugs.IsValid(request.Slug), "slug", "Slug may contain lower-case letters, digits and single hyphens.")
            .Check(request.Name is null || request.Slug is not null || Slugs.Create(request.Name).Length > 0, "slug", "A slug cannot be derived from this name; provide one.");

    private static StoreProductDetails ToDetails(
        string name, string? slug, string? description, decimal price, decimal vatRate, bool isVisible, int sortOrder) =>
        new(name, slug ?? Slugs.Create(name), description, price, vatRate, isVisible, sortOrder);

    private static ProblemHttpResult SlugTaken() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The slug is already used in this store");

    private static ValidationProblem NoSuchParent() =>
        new RequestErrors().Check(false, "parentId", "That parent category was not found.").ToProblem();

    private static ValidationProblem ItsOwnParent() =>
        new RequestErrors().Check(false, "parentId", "A category cannot be its own parent.").ToProblem();

    private static ValidationProblem WouldBeItsOwnAncestor() =>
        new RequestErrors().Check(false, "parentId", "A category cannot sit beneath one of its own.").ToProblem();

    private static ValidationProblem TooDeep() =>
        new RequestErrors().Check(false, "parentId", $"Categories can be nested up to {CategoryTree.MaxDepth} levels deep.").ToProblem();
}

internal sealed record ListProductRequest(Guid ProductId, string? Name, string? Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder);

internal sealed record UpdateStoreProductRequest(
    string? Name,
    string? Slug,
    string? Description,
    decimal Price,
    decimal VatRate,
    bool IsVisible,
    int SortOrder,
    ListingSeoRequest? Seo = null);

// What this page says for itself. Absent leaves what is there alone; present replaces all of it (D-165).
internal sealed record ListingSeoRequest(string? Title, string? Description, string? SocialImageUrl, bool NoIndex);

internal sealed record CategorySeoRequest(string? Title, string? Description, string? PageText);

internal sealed record AssignCategoriesRequest(List<Guid>? CategoryIds);

internal sealed record CategoryRequest(string? Name, string? Slug, int SortOrder, CategorySeoRequest? Seo = null, Guid? ParentId = null);

internal sealed record AdminCategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    int SortOrder,
    Guid? ParentId,
    List<Guid> AttributeIds,
    string? SeoTitle,
    string? SeoDescription,
    string? PageText);

internal sealed record AdminStoreProductResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    string Slug,
    string? Description,
    decimal Price,
    decimal VatRate,
    bool IsVisible,
    int SortOrder,
    List<Guid> CategoryIds,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoSocialImageUrl,
    bool SeoNoIndex)
{
    public static AdminStoreProductResponse From(StoreProduct storeProduct, Product product) => new(
        storeProduct.Id,
        product.Id,
        product.Default.Sku,
        storeProduct.Name,
        storeProduct.Slug,
        storeProduct.Description,
        storeProduct.Price,
        storeProduct.VatRate,
        storeProduct.IsVisible,
        storeProduct.SortOrder,
        storeProduct.Categories.Select(assignment => assignment.CategoryId).ToList(),
        storeProduct.SeoTitle,
        storeProduct.SeoDescription,
        storeProduct.SeoSocialImageUrl,
        storeProduct.SeoNoIndex);
}
