using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Connections;
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
        IProviderConnections connections,
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
            store.LogoPath is null ? null : "/api/storefront/store/logo",
            store.ReturnWindowDays,
            new StorefrontThemeResponse(store.Theme.PrimaryColor, store.Theme.SecondaryColor, store.Theme.BorderRadius),
            // The keys a provider issued to be used in the shopper's own browser — a carrier's map widget needs
            // one before it will open. Only these; a credential has no way into this response.
            [.. (await connections.PublishedKeysAsync(cancellationToken))
                .Select(published => new StorefrontProviderKeyResponse(published.Provider, published.Key))]));
    }
}

internal sealed record StorefrontStoreResponse(
    Guid Id,
    string Name,
    string Currency,
    string Culture,
    string? LogoUrl,
    int ReturnWindowDays,
    StorefrontThemeResponse Theme,
    IReadOnlyList<StorefrontProviderKeyResponse> ProviderKeys);

internal sealed record StorefrontProviderKeyResponse(string Provider, string Key);

internal sealed record StorefrontThemeResponse(string PrimaryColor, string SecondaryColor, int BorderRadius);
