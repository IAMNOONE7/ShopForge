using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Bulk;

// Changing one thing across many listings. Four narrow actions rather than one that takes everything: a
// merchant hiding forty things and a merchant raising their prices are doing different jobs, and a single
// endpoint that did both would validate for neither (D-183).
//
// Each applies to the listings it finds and says how many it was given and how many it changed. A selection
// of a hundred where a colleague has deleted one must not lose the other ninety-nine, and a count that comes
// back smaller than the one that went out is how a screen knows to reload.
internal static class AdminBulkListingEndpoints
{
    // More than this in one request is a script rather than a screen, and a screen is what this is for.
    private const int Most = 500;

    public static IEndpointRouteBuilder MapAdminBulkListings(this IEndpointRouteBuilder storeAdmin)
    {
        var bulk = storeAdmin.MapGroup("/products/bulk").RequireAuthorization(AdminPolicies.CatalogManagement);

        bulk.MapPost("/visibility", VisibilityAsync);
        bulk.MapPost("/price", PriceAsync);
        bulk.MapPost("/categories", CategoriesAsync);
        bulk.MapPost("/archive", ArchiveAsync);

        return storeAdmin;
    }

    private static async Task<Results<Ok<BulkResultResponse>, ValidationProblem>> VisibilityAsync(
        BulkVisibilityRequest request,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (Refused(request.StoreProductIds) is { } errors)
        {
            return errors;
        }

        var listings = await ChosenAsync(dbContext, request.StoreProductIds!, cancellationToken);
        var changed = listings.Count(listing => listing.SetVisible(request.IsVisible));

        return await SavedAsync(
            dbContext, audit, "catalog.listings.visibility", request.StoreProductIds!.Count, changed,
            $"{(request.IsVisible ? "shown" : "hidden")} {changed}", cancellationToken);
    }

    // Either a price for all of them or a percentage off what each one costs now. Setting the same number on
    // a hundred different things is rarely what anybody means; moving them all by five per cent is.
    private static async Task<Results<Ok<BulkResultResponse>, ValidationProblem>> PriceAsync(
        BulkPriceRequest request,
        DbContext dbContext,
        IAuditLog audit,
        ICurrentStoreSettings storeSettings,
        CancellationToken cancellationToken)
    {
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var errors = Errors(request.StoreProductIds)
            .Check(request.Set is not null ^ request.ByPercent is not null, "price", "Give either a price to set or a percentage to change by.")
            .Check(request.Set is null || (request.Set >= 0 && currency.Holds(request.Set.Value)), "set", $"A price must be zero or more, with at most {currency.Decimals} decimals.")
            .Check(request.ByPercent is null or (> -100 and <= 1000), "byPercent", "A percentage change must be more than -100 and at most 1000.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var listings = await ChosenAsync(dbContext, request.StoreProductIds!, cancellationToken);
        var changed = 0;

        foreach (var listing in listings)
        {
            // Rounded to the money it will be charged in, away from zero, so a five per cent rise on 99.99
            // is 104.99 in euros and 105 in yen, and never something with decimals the currency has not got
            // (D-183, D-186).
            var price = request.Set ?? currency.Round(listing.Price * (1 + (request.ByPercent!.Value / 100m)));

            if (listing.SetPrice(decimal.Max(price, 0m), currency))
            {
                changed++;
            }
        }

        var what = request.Set is { } set ? $"set {changed} to {set}" : $"changed {changed} by {request.ByPercent}%";

        return await SavedAsync(dbContext, audit, "catalog.listings.price", request.StoreProductIds!.Count, changed, what, cancellationToken);
    }

    // Adding to a category and taking out of one, never replacing: a merchant putting forty things into
    // "Sale" does not mean taking them out of everything else they are in.
    private static async Task<Results<Ok<BulkResultResponse>, ValidationProblem>> CategoriesAsync(
        BulkCategoriesRequest request,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var adding = request.AddCategoryIds ?? [];
        var removing = request.RemoveCategoryIds ?? [];
        var errors = Errors(request.StoreProductIds)
            .Check(adding.Count > 0 || removing.Count > 0, "categoryIds", "Name at least one category to add to or remove from.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        // Categories of this store, which is what the tenancy filter leaves; a category of another's is
        // simply not found and so cannot be assigned.
        var categories = await dbContext.Set<Category>()
            .Where(category => adding.Contains(category.Id))
            .ToListAsync(cancellationToken);

        var errorsOnCategories = new RequestErrors()
            .Check(categories.Count == adding.Count, "addCategoryIds", "One or more categories were not found.");

        if (errorsOnCategories.Any)
        {
            return errorsOnCategories.ToProblem();
        }

        var listings = await ChosenAsync(dbContext, request.StoreProductIds!, cancellationToken);
        var changed = listings.Count(listing => listing.AddToCategories(categories) | listing.RemoveFromCategories(removing));

        return await SavedAsync(
            dbContext, audit, "catalog.listings.categories", request.StoreProductIds!.Count, changed,
            $"changed {changed}", cancellationToken);
    }

    // Retiring a range. 27b made archiving the way to take something out of use; doing it one listing at a
    // time for a discontinued range rather defeats that (D-180).
    private static async Task<Results<Ok<BulkResultResponse>, ValidationProblem>> ArchiveAsync(
        BulkArchiveRequest request,
        DbContext dbContext,
        TimeProvider clock,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (Refused(request.StoreProductIds) is { } errors)
        {
            return errors;
        }

        var listings = await dbContext.Set<StoreProduct>()
            .IgnoreQueryFilters([TenancyFilters.Archived])
            .Where(listing => request.StoreProductIds!.Contains(listing.Id))
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var changed = listings.Count(listing => request.Archived ? listing.Archive(now) : listing.Restore());

        return await SavedAsync(
            dbContext, audit, "catalog.listings.archive", request.StoreProductIds!.Count, changed,
            $"{(request.Archived ? "archived" : "restored")} {changed}", cancellationToken);
    }

    // The listings the store has, out of the ones named. Another store's ids are not found, which is the
    // tenancy filter doing the work rather than a check somebody has to remember.
    private static async Task<List<StoreProduct>> ChosenAsync(
        DbContext dbContext, List<Guid> storeProductIds, CancellationToken cancellationToken) =>
        await dbContext.Set<StoreProduct>()
            .Include(listing => listing.Categories)
            .Where(listing => storeProductIds.Contains(listing.Id))
            .ToListAsync(cancellationToken);

    // One entry for one decision. A hundred entries for "raised these prices by five per cent" is the same
    // fact written a hundred times, and it buries everything else in the log (D-183).
    private static async Task<Results<Ok<BulkResultResponse>, ValidationProblem>> SavedAsync(
        DbContext dbContext,
        IAuditLog audit,
        string action,
        int asked,
        int changed,
        string what,
        CancellationToken cancellationToken)
    {
        if (changed > 0)
        {
            audit.Record(action, what, new { Asked = asked, Changed = changed });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new BulkResultResponse(asked, changed));
    }

    private static ValidationProblem? Refused(List<Guid>? ids)
    {
        var errors = Errors(ids);

        return errors.Any ? errors.ToProblem() : null;
    }

    private static RequestErrors Errors(List<Guid>? ids) =>
        new RequestErrors()
            .Check(ids is { Count: > 0 }, "storeProductIds", "Name at least one listing.")
            .Check(ids is null || ids.Count <= Most, "storeProductIds", $"Up to {Most} listings in one action.");
}

internal sealed record BulkVisibilityRequest(List<Guid>? StoreProductIds, bool IsVisible);

internal sealed record BulkPriceRequest(List<Guid>? StoreProductIds, decimal? Set, decimal? ByPercent);

internal sealed record BulkCategoriesRequest(List<Guid>? StoreProductIds, List<Guid>? AddCategoryIds, List<Guid>? RemoveCategoryIds);

internal sealed record BulkArchiveRequest(List<Guid>? StoreProductIds, bool Archived);

// How many were named and how many actually changed. The difference is a listing that was already as asked,
// or one this store does not have.
internal sealed record BulkResultResponse(int Asked, int Changed);
