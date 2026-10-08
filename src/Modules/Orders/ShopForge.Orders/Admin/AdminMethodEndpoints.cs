using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Payments;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Http;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;
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

    private static Ok<List<string>> GetPaymentProviders(IEnumerable<IPaymentProvider> providers) =>
        TypedResults.Ok(providers.Select(provider => provider.Key).Order(StringComparer.Ordinal).ToList());

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
        var errors = ValidateName(request.Name)
            .Check(code is not null, "name", "The name must contain letters or digits.")
            .Check(providers.Any(provider => provider.Key == providerKey), "providerKey", "That payment provider is not available.");

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

    private static Ok<List<string>> GetShippingProviders(IEnumerable<IShippingProvider> providers) =>
        TypedResults.Ok(providers.Select(provider => provider.Key).Order(StringComparer.Ordinal).ToList());

    private static async Task<Ok<List<AdminShippingMethodResponse>>> GetShippingMethodsAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<ShippingMethod>()
            .OrderBy(method => method.Price)
            .ThenBy(method => method.Name)
            .Select(method => new AdminShippingMethodResponse(
                method.Code,
                method.Name,
                method.ProviderKey,
                method.Price,
                method.VatRate,
                method.IsActive,
                method.RequiresPickupPoint,
                method.MaxWeightGrams,
                method.Countries))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<AdminShippingMethodResponse>, ValidationProblem, ProblemHttpResult>> CreateShippingMethodAsync(
        ShippingMethodRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IEnumerable<IShippingProvider> providers,
        ICurrentStoreSettings storeSettings,
        CancellationToken cancellationToken)
    {
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var code = Codes.Of(request.Name);
        var providerKey = request.ProviderKey ?? StoreShippingProvider.ProviderKey;
        var errors = ValidateShipping(request, currency)
            .Check(code is not null, "name", "The name must contain letters or digits.")
            .Check(providers.Any(provider => provider.Key == providerKey), "providerKey", "That shipping provider is not available.");

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
            request.Countries);
        dbContext.Add(method);
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
        CancellationToken cancellationToken)
    {
        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var errors = ValidateShipping(request, currency);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var method = await dbContext.Set<ShippingMethod>().SingleOrDefaultAsync(method => method.Code == code, cancellationToken);

        if (method is null)
        {
            return TypedResults.NotFound();
        }

        method.Update(
            request.Name!,
            request.Price,
            request.VatRate,
            request.IsActive,
            request.RequiresPickupPoint,
            currency,
            request.MaxWeightGrams,
            request.Countries);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ShippingResponse(method));
    }

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
            method.Countries);

    private static RequestErrors ValidateName(string? name) =>
        new RequestErrors().Check(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100, "name", "Name is required (up to 100 characters).");

    private static RequestErrors ValidateShipping(ShippingMethodRequest request, Currency currency) =>
        ValidateName(request.Name)
            .Check(request.Price >= 0 && currency.Holds(request.Price), "price", $"Price must be zero or more, with at most {currency.Decimals} decimals.")
            .Check(request.VatRate is >= 0 and <= 100, "vatRate", "The VAT rate must be between 0 and 100.")
            .Check(request.MaxWeightGrams is null or > 0, "maxWeightGrams", "A weight limit must be more than nothing.")
            .Check(
                request.Countries is null || request.Countries.All(country => country.Trim().Length == 2),
                "countries",
                "Each country must be a two-letter code.");

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
    IReadOnlyList<string>? Countries = null);

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
    IReadOnlyList<string> Countries);

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
