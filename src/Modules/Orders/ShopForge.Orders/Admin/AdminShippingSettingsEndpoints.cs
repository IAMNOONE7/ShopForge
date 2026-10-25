using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Admin;

// The few numbers a store's parcels are estimated from (D-218), and the carrier services its methods can send by.
internal static class AdminShippingSettingsEndpoints
{
    public static void MapAdminShippingSettings(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/shipping-settings", GetSettingsAsync);
        storeAdmin.MapPut("/shipping-settings", UpdateSettingsAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapGet("/carrier-services", GetCarrierServicesAsync);
    }

    private static async Task<Ok<ShippingSettingsResponse>> GetSettingsAsync(DbContext dbContext, IStoreContext storeContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(ShippingSettingsResponse.From(await SettingsOfAsync(dbContext, storeContext, cancellationToken)));

    private static async Task<Results<Ok<ShippingSettingsResponse>, ValidationProblem>> UpdateSettingsAsync(
        ShippingSettingsRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(
                request.PackagingWeightGrams is >= 0 and <= ShippingSettings.MostPackagingWeightGrams,
                "packagingWeightGrams",
                $"Packaging weighs from 0 to {ShippingSettings.MostPackagingWeightGrams} grams.")
            .Check(request.FillPercent is >= 1 and <= 100, "fillPercent", "A parcel is filled to a share of 1 to 100 per cent.")
            .Check(
                request.LargestParcelOrder is >= 1 and <= ParcelEstimator.MostParcels,
                "largestParcelOrder",
                $"The largest order sent by parcel is from 1 to {ParcelEstimator.MostParcels} parcels.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var settings = await SettingsOfAsync(dbContext, storeContext, cancellationToken);
        var before = ShippingSettingsResponse.From(settings);

        if (dbContext.Entry(settings).State == EntityState.Detached)
        {
            dbContext.Add(settings);
        }

        settings.Change(request.PackagingWeightGrams!.Value, request.FillPercent!.Value, request.LargestParcelOrder!.Value);
        var after = ShippingSettingsResponse.From(settings);

        if (after != before)
        {
            audit.Record("shipping.settings-changed", "Shipping settings", new { Before = before, After = after });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(after);
    }

    // What a store's method can send by: the services in use, of the providers this platform offers its stores.
    private static async Task<Ok<List<CarrierServiceResponse>>> GetCarrierServicesAsync(
        DbContext dbContext,
        IEnumerable<IShippingProvider> providers,
        CancellationToken cancellationToken)
    {
        var offered = providers.Where(provider => provider.IsOfferedToStores).Select(provider => provider.Key).ToList();
        var services = await dbContext.Set<CarrierService>()
            .AsNoTracking()
            .Where(service => service.IsActive && offered.Contains(service.ProviderKey))
            .OrderBy(service => service.Key)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(services.Select(CarrierServiceResponse.From).ToList());
    }

    internal static async Task<ShippingSettings> SettingsOfAsync(DbContext dbContext, IStoreContext storeContext, CancellationToken cancellationToken) =>
        await dbContext.Set<ShippingSettings>().SingleOrDefaultAsync(cancellationToken) ?? new ShippingSettings(storeContext.StoreId!.Value);
}

internal sealed record ShippingSettingsRequest(int? PackagingWeightGrams, int? FillPercent, int? LargestParcelOrder);

internal sealed record ShippingSettingsResponse(int PackagingWeightGrams, int FillPercent, int LargestParcelOrder)
{
    public static ShippingSettingsResponse From(ShippingSettings settings) =>
        new(settings.PackagingWeightGrams, settings.FillPercent, settings.LargestParcelOrder);
}
