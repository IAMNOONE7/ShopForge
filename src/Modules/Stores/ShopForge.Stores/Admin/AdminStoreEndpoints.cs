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
                store.Status,
                new AdminThemeResponse(store.Theme.PrimaryColor, store.Theme.SecondaryColor, store.Theme.BorderRadius),
                store.LogoPath == null ? null : "/api/admin/stores/" + store.Id + "/logo",
                store.Domains.Where(domain => domain.IsPrimary).Select(domain => domain.HostName).FirstOrDefault(),
                store.ReturnWindowDays,
                store.Company == null
                    ? null
                    : new AdminCompanyResponse(
                        store.Company.LegalName,
                        store.Company.Line1,
                        store.Company.City,
                        store.Company.PostalCode,
                        store.Company.Country,
                        store.Company.RegistrationNumber,
                        store.Company.VatNumber)))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(stores);
    }
}

internal sealed record AdminStoreResponse(
    Guid Id,
    string Name,
    string Currency,
    string Culture,
    StoreStatus Status,
    AdminThemeResponse Theme,
    string? LogoUrl,
    string? PrimaryHostName,
    int ReturnWindowDays,
    AdminCompanyResponse? Company)
{
    public static AdminStoreResponse From(Store store, string? primaryHostName) => new(
        store.Id,
        store.Name,
        store.Currency,
        store.Culture,
        store.Status,
        new AdminThemeResponse(store.Theme.PrimaryColor, store.Theme.SecondaryColor, store.Theme.BorderRadius),
        store.LogoPath is null ? null : $"/api/admin/stores/{store.Id}/logo",
        primaryHostName,
        store.ReturnWindowDays,
        store.Company is null
            ? null
            : new AdminCompanyResponse(
                store.Company.LegalName,
                store.Company.Line1,
                store.Company.City,
                store.Company.PostalCode,
                store.Company.Country,
                store.Company.RegistrationNumber,
                store.Company.VatNumber));
}

internal sealed record AdminCompanyResponse(
    string LegalName,
    string Line1,
    string City,
    string PostalCode,
    string Country,
    string RegistrationNumber,
    string? VatNumber);

internal sealed record AdminThemeResponse(string PrimaryColor, string SecondaryColor, int BorderRadius);
