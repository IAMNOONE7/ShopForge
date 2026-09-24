using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Admin;

internal static class AdminDiscountEndpoints
{
    public static void MapAdminDiscounts(this IEndpointRouteBuilder storeAdmin)
    {
        var discounts = storeAdmin.MapGroup("/discounts");

        discounts.MapGet("/", GetDiscountsAsync);
        discounts.MapPost("/", CreateDiscountAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        discounts.MapPut("/{code}", UpdateDiscountAsync).RequireAuthorization(AdminPolicies.StoreManagement);
    }

    private static async Task<Ok<List<AdminDiscountResponse>>> GetDiscountsAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<Discount>()
            .OrderBy(discount => discount.Code)
            .Select(discount => new AdminDiscountResponse(
                discount.Code,
                discount.Name,
                discount.Kind.ToString(),
                discount.Value,
                discount.MinimumOrderAmount,
                discount.StartsAt,
                discount.EndsAt,
                discount.MaxRedemptions,
                discount.MaxRedemptionsPerCustomer,
                discount.Redemptions,
                discount.IsActive))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<AdminDiscountResponse>, ValidationProblem, ProblemHttpResult>> CreateDiscountAsync(
        DiscountRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var code = Discount.Normalize(request.Code!);

        if (await dbContext.Set<Discount>().AnyAsync(discount => discount.Code == code, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The store already has this code");
        }

        var discount = new Discount(storeContext.StoreId!.Value, code, request.Name!, request.ToKind(), request.Value, request.ToLimits());
        dbContext.Add(discount);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{discount.StoreId}/discounts/{discount.Code}", Response(discount));
    }

    // The code and its kind are fixed once it is out in the world; everything else can be corrected.
    private static async Task<Results<Ok<AdminDiscountResponse>, ValidationProblem, NotFound>> UpdateDiscountAsync(
        string code,
        DiscountRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var normalized = Discount.Normalize(code);
        var discount = await dbContext.Set<Discount>().SingleOrDefaultAsync(candidate => candidate.Code == normalized, cancellationToken);

        if (discount is null)
        {
            return TypedResults.NotFound();
        }

        discount.Update(request.Name!, discount.Kind, request.Value, request.ToLimits(), request.IsActive);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(Response(discount));
    }

    private static RequestErrors Validate(DiscountRequest request) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(request.Code) && request.Code.Trim().Length <= 40, "code", "A code of up to 40 characters is required.")
            .Check(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 100, "name", "Name is required (up to 100 characters).")
            .Check(request.ToKind() != DiscountKind.Percentage || request.Value is > 0 and <= 100, "value", "A percentage is between 0 and 100.")
            .Check(
                request.ToKind() != DiscountKind.Amount || (request.Value > 0 && decimal.Round(request.Value, 2) == request.Value),
                "value",
                "An amount is positive, with at most two decimals.")
            .Check(request.MinimumOrderAmount is null or >= 0, "minimumOrderAmount", "A minimum cannot be negative.")
            .Check(request.MaxRedemptions is null or > 0, "maxRedemptions", "A usage limit is at least one.")
            .Check(request.MaxRedemptionsPerCustomer is null or > 0, "maxRedemptionsPerCustomer", "A per-customer limit is at least one.")
            .Check(
                request.StartsAt is null || request.EndsAt is null || request.StartsAt < request.EndsAt,
                "endsAt",
                "The code has to end after it starts.");

    private static AdminDiscountResponse Response(Discount discount) => new(
        discount.Code,
        discount.Name,
        discount.Kind.ToString(),
        discount.Value,
        discount.MinimumOrderAmount,
        discount.StartsAt,
        discount.EndsAt,
        discount.MaxRedemptions,
        discount.MaxRedemptionsPerCustomer,
        discount.Redemptions,
        discount.IsActive);
}

internal sealed record DiscountRequest(
    string? Code,
    string? Name,
    string? Kind,
    decimal Value,
    decimal? MinimumOrderAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? MaxRedemptions,
    int? MaxRedemptionsPerCustomer,
    bool IsActive)
{
    public DiscountKind ToKind() => Enum.TryParse<DiscountKind>(Kind, ignoreCase: true, out var kind) ? kind : DiscountKind.Percentage;

    public DiscountLimits ToLimits() =>
        new(MinimumOrderAmount, StartsAt, EndsAt, MaxRedemptions, MaxRedemptionsPerCustomer);
}

internal sealed record AdminDiscountResponse(
    string Code,
    string Name,
    string Kind,
    decimal Value,
    decimal? MinimumOrderAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? MaxRedemptions,
    int? MaxRedemptionsPerCustomer,
    int Redemptions,
    bool IsActive);
