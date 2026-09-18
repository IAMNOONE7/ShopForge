using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Storefront;

internal static class StorefrontStoreEndpoint
{
    public static RouteHandlerBuilder MapStorefrontStore(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/store", GetStoreAsync);

    private static async Task<Results<Ok<StorefrontStoreResponse>, NotFound>> GetStoreAsync(
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var store = await dbContext.Set<Store>()
            .AsNoTracking()
            .SingleOrDefaultAsync(store => store.Id == storeContext.StoreId, cancellationToken);

        if (store is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new StorefrontStoreResponse(
            store.Id,
            store.Name,
            store.Currency,
            store.Culture,
            new StorefrontThemeResponse(store.Theme.PrimaryColor, store.Theme.SecondaryColor, store.Theme.BorderRadius)));
    }
}

internal sealed record StorefrontStoreResponse(Guid Id, string Name, string Currency, string Culture, StorefrontThemeResponse Theme);

internal sealed record StorefrontThemeResponse(string PrimaryColor, string SecondaryColor, int BorderRadius);
