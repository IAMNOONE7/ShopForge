using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Wishlist;

// A list of what a customer wants, which needs an account: there is no cookie-based wishlist (D-090).
internal static class WishlistEndpoints
{
    public static void MapWishlist(this IEndpointRouteBuilder storefront)
    {
        var wishlist = storefront.MapGroup("/account/wishlist").RequireAuthorization(CustomerPolicies.Customer);

        wishlist.MapGet("/", GetWishlistAsync);
        wishlist.MapPost("/", AddAsync);
        wishlist.MapDelete("/{storeProductId:guid}", RemoveAsync);
    }

    private static async Task<Results<Ok<List<WishlistItemResponse>>, UnauthorizedHttpResult>> GetWishlistAsync(
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        ISellableProducts products,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        var wanted = await dbContext.Set<WishlistItem>()
            .AsNoTracking()
            .Where(item => item.StoreCustomerId == storeCustomerId)
            .OrderByDescending(item => item.AddedAt)
            .Select(item => item.StoreProductId)
            .ToListAsync(cancellationToken);

        // A product that stopped being sold simply drops out, the way it drops out of a cart; the row stays.
        var sellable = await products.FindAsync(wanted, cancellationToken);

        return TypedResults.Ok(wanted
            .Select(storeProductId => sellable.SingleOrDefault(product => product.StoreProductId == storeProductId))
            .OfType<SellableProduct>()
            .Select(product => new WishlistItemResponse(product.StoreProductId, product.Name, product.Slug, product.Price, product.ImageUrl))
            .ToList());
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> AddAsync(
        WishlistRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        ICurrentCustomer currentCustomer,
        ISellableProducts products,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        if ((await products.FindAsync([request.StoreProductId], cancellationToken)).Count == 0)
        {
            return TypedResults.NotFound();
        }

        var alreadyThere = await dbContext.Set<WishlistItem>().AnyAsync(
            item => item.StoreCustomerId == storeCustomerId && item.StoreProductId == request.StoreProductId,
            cancellationToken);

        if (!alreadyThere)
        {
            dbContext.Add(new WishlistItem(storeContext.StoreId!.Value, storeCustomerId, request.StoreProductId, clock.GetUtcNow()));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> RemoveAsync(
        Guid storeProductId,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        await dbContext.Set<WishlistItem>()
            .Where(item => item.StoreCustomerId == storeCustomerId && item.StoreProductId == storeProductId)
            .ExecuteDeleteAsync(cancellationToken);

        return TypedResults.NoContent();
    }
}

internal sealed record WishlistRequest(Guid StoreProductId);

internal sealed record WishlistItemResponse(Guid StoreProductId, string Name, string Slug, decimal Price, string? ImageUrl);
