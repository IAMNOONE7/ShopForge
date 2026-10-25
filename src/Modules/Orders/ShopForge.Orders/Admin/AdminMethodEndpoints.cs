using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Payments;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Http;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tax;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Admin;

internal static class AdminMethodEndpoints
{
    public static void MapAdminMethods(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/payment-providers", GetPaymentProviders);
        storeAdmin.MapGet("/payment-methods", GetPaymentMethodsAsync);
        storeAdmin.MapPost("/payment-methods", CreatePaymentMethodAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPut("/payment-methods/{code}", UpdatePaymentMethodAsync).RequireAuthorization(AdminPolicies.StoreManagement);

        storeAdmin.MapGet("/shipping-providers", GetShippingProviders);
        storeAdmin.MapGet("/shipping-methods", GetShippingMethodsAsync);
        storeAdmin.MapPost("/shipping-methods", CreateShippingMethodAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPut("/shipping-methods/{code}", UpdateShippingMethodAsync).RequireAuthorization(AdminPolicies.StoreManagement);
    }

    private static Ok<List<string>> GetPaymentProviders(IEnumerable<IPaymentProvider> providers) => Offered(providers);

    private static async Task<Ok<List<AdminPaymentMethodResponse>>> GetPaymentMethodsAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<PaymentMethod>()
            .OrderBy(method => method.Name)
            .Select(method => new AdminPaymentMethodResponse(method.Code, method.Name, method.ProviderKey, method.IsActive))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<AdminPaymentMethodResponse>, ValidationProblem, ProblemHttpResult>> CreatePaymentMethodAsync(
        PaymentMethodRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IEnumerable<IPaymentProvider> providers,
        CancellationToken cancellationToken)
    {
        var code = Codes.Of(request.Name);
        var providerKey = request.ProviderKey ?? ManualPaymentProvider.ProviderKey;
        var refusal = Refusal(providers, providerKey, "payment");
        var errors = ValidateName(request.Name)
            .Check(code is not null, "name", "The name must contain letters or digits.")
            .Check(refusal is null, "providerKey", refusal ?? string.Empty);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<PaymentMethod>().AnyAsync(method => method.Code == code, cancellationToken))
        {
            return Conflict("payment");
        }

        // The provider is fixed once payments have run through a method; a store adds another method instead.
        var method = new PaymentMethod(storeContext.StoreId!.Value, code!, request.Name!, providerKey);
        dbContext.Add(method);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/admin/stores/{method.StoreId}/payment-methods/{method.Code}",
            new AdminPaymentMethodResponse(method.Code, method.Name, method.ProviderKey, method.IsActive));
    }

    private static async Task<Results<Ok<AdminPaymentMethodResponse>, ValidationProblem, NotFound>> UpdatePaymentMethodAsync(
        string code,
        PaymentMethodRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateName(request.Name);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var method = await dbContext.Set<PaymentMethod>().SingleOrDefaultAsync(method => method.Code == code, cancellationToken);

        if (method is null)
        {
            return TypedResults.NotFound();
        }

        method.Update(request.Name!, request.IsActive);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AdminPaymentMethodResponse(method.Code, method.Name, method.ProviderKey, method.IsActive));
    }

    private static Ok<List<string>> GetShippingProviders(IEnumerable<IShippingProvider> providers) => Offered(providers);

    // What a store may choose from. A platform account is registered wherever the deployment has its keys, and is
    // still not a store's to choose (D-196).
    private static Ok<List<string>> Offered<TProvider>(IEnumerable<TProvider> providers)
        where TProvider : IConnectedProvider =>
        TypedResults.Ok(providers.Where(provider => provider.IsOfferedToStores).Select(provider => provider.Key).Order(StringComparer.Ordinal).ToList());

    private static string? Refusal<TProvider>(IEnumerable<TProvider> providers, string providerKey, string kind)
        where TProvider : IConnectedProvider =>
        providers.FirstOrDefault(provider => provider.Key == providerKey) switch
        {
            null => $"That {kind} provider is not available.",
            { IsOfferedToStores: false } => $"That {kind} provider is the platform's own account rather than a store's, so a store cannot use it.",
            _ => null,
        };

    private static async Task<Ok<List<AdminShippingMethodResponse>>> GetShippingMethodsAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var methods = await dbContext.Set<ShippingMethod>()
            .AsNoTracking()
            .OrderBy(method => method.Price)
            .ThenBy(method => method.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(methods.Select(ShippingResponse).ToList());
    }

    private static async Task<Results<Created<AdminShippingMethodResponse>, ValidationProblem, ProblemHttpResult>> CreateShippingMethodAsync(
        ShippingMethodRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IEnumerable<IShippingProvider> providers,
        ICurrentStoreSettings storeSettings,
        ITaxRates taxRates,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var code = Codes.Of(request.Name);
        var providerKey = request.ProviderKey ?? StoreShippingProvider.ProviderKey;
        var refusal = Refusal(providers, providerKey, "shipping");
        var pricing = request.Pricing ?? ShippingPricing.Flat;
        var service = await ServiceAsync(dbContext, request.CarrierService, cancellationToken);
        var errors = ValidateShipping(request, currency, await taxRates.ClassesAsync(cancellationToken))
            .Check(code is not null, "name", "The name must contain letters or digits.")
            .Check(refusal is null, "providerKey", refusal ?? string.Empty);
        Delivery(errors, request, pricing, request.CarrierService, service, choosing: true, providerKey);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<ShippingMethod>().AnyAsync(method => method.Code == code, cancellationToken))
        {
            return Conflict("shipping");
        }

        var method = new ShippingMethod(
            storeContext.StoreId!.Value,
            code!,
            request.Name!,
            providerKey,
            request.Price,
            request.VatRate,
            currency,
            request.RequiresPickupPoint,
            request.MaxWeightGrams,
            request.Countries,
            request.TaxClass);
        method.SendBy(pricing, service?.Key, request.ParcelMaxWeightGrams, ParcelSides(request));
        dbContext.Add(method);
        audit.Record("shipping-method.added", method.Name, Terms(method));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/admin/stores/{method.StoreId}/shipping-methods/{method.Code}",
            ShippingResponse(method));
    }

    private static async Task<Results<Ok<AdminShippingMethodResponse>, ValidationProblem, NotFound>> UpdateShippingMethodAsync(
        string code,
        ShippingMethodRequest request,
        DbContext dbContext,
        ICurrentStoreSettings storeSettings,
        ITaxRates taxRates,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var errors = ValidateShipping(request, currency, await taxRates.ClassesAsync(cancellationToken));

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var method = await dbContext.Set<ShippingMethod>().SingleOrDefaultAsync(method => method.Code == code, cancellationToken);

        if (method is null)
        {
            return TypedResults.NotFound();
        }

        // The delivery terms come together. A request that leaves out the pricing keeps them as they are, so a form
        // that knows nothing of them cannot turn a method priced by the parcel back into a flat one (D-218).
        var choosing = request.Pricing is not null;
        var pricing = request.Pricing ?? method.Pricing;
        var serviceKey = choosing ? request.CarrierService : method.CarrierServiceKey;
        var service = await ServiceAsync(dbContext, serviceKey, cancellationToken);
        errors
            .Check(
                choosing || (request.CarrierService is null && request.ParcelMaxWeightGrams is null && ParcelSidesGiven(request) == 0),
                "pricing",
                "Send the pricing with the carrier service and the parcel limits: they are set together.");
        Delivery(errors, request, pricing, serviceKey, service, choosing, method.ProviderKey);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var before = Terms(method);

        method.Update(
            request.Name!,
            request.Price,
            request.VatRate,
            request.IsActive,
            request.RequiresPickupPoint,
            currency,
            request.MaxWeightGrams,
            request.Countries,
            request.TaxClass);

        if (choosing)
        {
            method.SendBy(pricing, service?.Key, request.ParcelMaxWeightGrams, ParcelSides(request));
        }

        audit.Record("shipping-method.changed", method.Name, new { Before = before, After = Terms(method) });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ShippingResponse(method));
    }

    private static async Task<CarrierService?> ServiceAsync(DbContext dbContext, string? key, CancellationToken cancellationToken) =>
        key is null ? null : await dbContext.Set<CarrierService>().AsNoTracking().SingleOrDefaultAsync(service => service.Key == key, cancellationToken);

    // How a method is priced and what it sends by (D-218). A flat method stays as it was (D-145) and may name its
    // service. A method priced by the parcel sends by a carrier service of its own provider, to that service's country
    // and kind of place, and promises at most what the service takes, never a parcel it would turn away. A service is
    // checked for being in use when it is chosen; one switched off later is readiness's to name, so the method can
    // still be changed, or switched off.
    private static void Delivery(
        RequestErrors errors,
        ShippingMethodRequest request,
        ShippingPricing pricing,
        string? serviceKey,
        CarrierService? service,
        bool choosing,
        string providerKey)
    {
        const string Together = "The length, the width and the height go together: give all three, in millimetres.";
        var sides = ParcelSidesGiven(request);

        errors
            .Check(pricing == ShippingPricing.Flat || serviceKey is not null, "carrierService", "A method priced by the parcel sends by a carrier service; choose one.")
            .Check(serviceKey is null || (service is not null && (service.IsActive || !choosing)), "carrierService", "That carrier service is not one in use.")
            .Check(service is null || service.ProviderKey == providerKey, "carrierService", $"{service?.Name} is carried by {service?.ProviderKey}, and this method by {providerKey}.");

        if (pricing == ShippingPricing.Flat)
        {
            errors
                .Check(request.ParcelMaxWeightGrams is null, "parcelMaxWeightGrams", "Parcel limits belong to a method priced by the parcel.")
                .Check(sides == 0, "parcelLengthMm", "Parcel limits belong to a method priced by the parcel.");
            return;
        }

        if (service is null)
        {
            return;
        }

        errors
            .Check(
                request.RequiresPickupPoint == service.DeliversToPoint,
                "requiresPickupPoint",
                service.DeliversToPoint
                    ? $"{service.Name} delivers to a place the shopper chooses, so the method needs a pickup point."
                    : $"{service.Name} delivers to the door, so the method takes no pickup point.")
            .Check(
                request.Countries is not { Count: > 0 } countries || countries.All(country => country.Trim().Equals(service.Country, StringComparison.OrdinalIgnoreCase)),
                "countries",
                $"{service.Name} delivers only to {service.Country}.")
            .Check(request.MaxWeightGrams is null, "maxWeightGrams", "A method priced by the parcel limits each parcel instead: use parcelMaxWeightGrams.");

        if (!choosing)
        {
            return;
        }

        var largest = ParcelSides(request);

        errors
            .Check(request.ParcelMaxWeightGrams is null or > 0, "parcelMaxWeightGrams", "A parcel's weight limit is more than nothing.")
            .Check(
                request.ParcelMaxWeightGrams is not > 0 || service.RefusesWeight(request.ParcelMaxWeightGrams.Value) is null,
                "parcelMaxWeightGrams",
                request.ParcelMaxWeightGrams is > 0 ? service.RefusesWeight(request.ParcelMaxWeightGrams.Value) ?? string.Empty : string.Empty)
            .Check(sides is 0 or 3, request.ParcelLengthMm is null ? "parcelLengthMm" : request.ParcelWidthMm is null ? "parcelWidthMm" : "parcelHeightMm", Together)
            .Check(sides != 3 || largest is not null, "parcelLengthMm", "A side is a whole number of millimetres from 1 to 10000.")
            .Check(largest is not { } given || service.RefusesLargest(given) is null, "parcelLengthMm", largest is { } named ? service.RefusesLargest(named) ?? string.Empty : string.Empty);
    }

    private static int ParcelSidesGiven(ShippingMethodRequest request) =>
        new[] { request.ParcelLengthMm, request.ParcelWidthMm, request.ParcelHeightMm }.Count(side => side is not null);

    private static Sides? ParcelSides(ShippingMethodRequest request) =>
        new SizeRequest(request.ParcelLengthMm, request.ParcelWidthMm, request.ParcelHeightMm).ToSides();

    private static object Terms(ShippingMethod method) =>
        new
        {
            Pricing = method.Pricing.ToString(),
            CarrierService = method.CarrierServiceKey,
            method.Price,
            method.IsActive,
            method.MaxWeightGrams,
            method.ParcelMaxWeightGrams,
            ParcelLargest = method.ParcelLargest is { } largest ? new[] { largest.Longest, largest.Middle, largest.Shortest } : null,
        };

    private static AdminShippingMethodResponse ShippingResponse(ShippingMethod method) =>
        new(
            method.Code,
            method.Name,
            method.ProviderKey,
            method.Price,
            method.VatRate,
            method.IsActive,
            method.RequiresPickupPoint,
            method.MaxWeightGrams,
            method.Countries,
            method.TaxClass,
            method.Pricing,
            method.CarrierServiceKey,
            method.ParcelMaxWeightGrams,
            method.ParcelLongestMm,
            method.ParcelMiddleMm,
            method.ParcelShortestMm);

    private static RequestErrors ValidateName(string? name) =>
        new RequestErrors().Check(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100, "name", "Name is required (up to 100 characters).");

    private static RequestErrors ValidateShipping(ShippingMethodRequest request, Currency currency, IReadOnlyList<string> taxClasses) =>
        ValidateName(request.Name)
            .Check(request.Price >= 0 && currency.Holds(request.Price), "price", $"Price must be zero or more, with at most {currency.Decimals} decimals.")
            .Check(request.VatRate is >= 0 and <= 100, "vatRate", "The VAT rate must be between 0 and 100.")
            .Check(request.MaxWeightGrams is null or > 0, "maxWeightGrams", "A weight limit must be more than nothing.")
            .Check(
                request.Countries is null || request.Countries.All(country => country.Trim().Length == 2),
                "countries",
                "Each country must be a two-letter code.")
            .Check(
                string.IsNullOrWhiteSpace(request.TaxClass) || taxClasses.Contains(request.TaxClass.Trim(), StringComparer.Ordinal),
                "taxClass",
                "That tax class is not one this store has.");

    private static ProblemHttpResult Conflict(string kind) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"The store already has a {kind} method with this name");
}

internal sealed record PaymentMethodRequest(string? Name, string? ProviderKey, bool IsActive);

internal sealed record ShippingMethodRequest(
    string? Name,
    string? ProviderKey,
    decimal Price,
    decimal VatRate,
    bool IsActive,
    bool RequiresPickupPoint,
    int? MaxWeightGrams = null,
    IReadOnlyList<string>? Countries = null,
    string? TaxClass = null,
    ShippingPricing? Pricing = null,
    string? CarrierService = null,
    int? ParcelMaxWeightGrams = null,
    int? ParcelLengthMm = null,
    int? ParcelWidthMm = null,
    int? ParcelHeightMm = null);

internal sealed record AdminPaymentMethodResponse(string Code, string Name, string ProviderKey, bool IsActive);

internal sealed record AdminShippingMethodResponse(
    string Code,
    string Name,
    string ProviderKey,
    decimal Price,
    decimal VatRate,
    bool IsActive,
    bool RequiresPickupPoint,
    int? MaxWeightGrams,
    IReadOnlyList<string> Countries,
    string? TaxClass,
    ShippingPricing Pricing,
    string? CarrierService,
    int? ParcelMaxWeightGrams,
    int? ParcelLengthMm,
    int? ParcelWidthMm,
    int? ParcelHeightMm);

internal static class Codes
{
    public static string? Of(string? name)
    {
        var code = new string((name ?? string.Empty).ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray())
            .Trim('-');

        while (code.Contains("--", StringComparison.Ordinal))
        {
            code = code.Replace("--", "-", StringComparison.Ordinal);
        }

        return code.Length is > 0 and <= 50 ? code : null;
    }
}
