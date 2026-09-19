using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Admin;

internal static class AdminStoreEndpoints
{
    public static RouteHandlerBuilder MapAdminStores(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/stores", GetStoresAsync);

    private static async Task<Ok<List<AdminStoreResponse>>> GetStoresAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        // The tenant filter still applies to stores; only the per-store filter on their domains is lifted.
        var stores = await dbContext.Set<Store>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .OrderBy(store => store.Name)
            .Select(store => new AdminStoreResponse(
                store.Id,
                store.Name,
                store.Currency,
                store.Culture,
                store.Domains.Where(domain => domain.IsPrimary).Select(domain => domain.HostName).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(stores);
    }
}

internal sealed record AdminStoreResponse(Guid Id, string Name, string Currency, string Culture, string? PrimaryHostName);
