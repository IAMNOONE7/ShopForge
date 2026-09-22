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

internal static class AdminPickupPointEndpoints
{
    public static void MapAdminPickupPoints(this IEndpointRouteBuilder storeAdmin)
    {
        var points = storeAdmin.MapGroup("/pickup-points");

        points.MapGet("/", GetPickupPointsAsync);
        points.MapPost("/", CreatePickupPointAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        points.MapPut("/{code}", UpdatePickupPointAsync).RequireAuthorization(AdminPolicies.StoreManagement);
    }

    private static async Task<Ok<List<AdminPickupPointResponse>>> GetPickupPointsAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<StorePickupPoint>()
            .OrderBy(point => point.Name)
            .Select(point => new AdminPickupPointResponse(
                point.Code,
                point.Name,
                point.Address.Line1,
                point.Address.City,
                point.Address.PostalCode,
                point.Address.Country,
                point.IsActive))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<AdminPickupPointResponse>, ValidationProblem, ProblemHttpResult>> CreatePickupPointAsync(
        PickupPointRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        var code = Codes.Of(request.Name);
        var errors = Validate(request).Check(code is not null, "name", "The name must contain letters or digits.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<StorePickupPoint>().AnyAsync(point => point.Code == code, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The store already has a pickup point with this name");
        }

        var point = new StorePickupPoint(storeContext.StoreId!.Value, code!, request.Name!, request.ToAddress(), request.IsActive);
        dbContext.Add(point);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{point.StoreId}/pickup-points/{point.Code}", Response(point));
    }

    private static async Task<Results<Ok<AdminPickupPointResponse>, ValidationProblem, NotFound>> UpdatePickupPointAsync(
        string code,
        PickupPointRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var point = await dbContext.Set<StorePickupPoint>().SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);

        if (point is null)
        {
            return TypedResults.NotFound();
        }

        point.Update(request.Name!, request.ToAddress(), request.IsActive);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(Response(point));
    }

    private static RequestErrors Validate(PickupPointRequest request) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 100, "name", "Name is required (up to 100 characters).")
            .Check(!string.IsNullOrWhiteSpace(request.Line1), "line1", "Street and number are required.")
            .Check(!string.IsNullOrWhiteSpace(request.City), "city", "City is required.")
            .Check(!string.IsNullOrWhiteSpace(request.PostalCode), "postalCode", "Postal code is required.")
            .Check(request.Country?.Trim().Length == 2, "country", "Country must be a two-letter code.");

    private static AdminPickupPointResponse Response(StorePickupPoint point) => new(
        point.Code,
        point.Name,
        point.Address.Line1,
        point.Address.City,
        point.Address.PostalCode,
        point.Address.Country,
        point.IsActive);
}

internal sealed record PickupPointRequest(string? Name, string? Line1, string? City, string? PostalCode, string? Country, bool IsActive)
{
    public Address ToAddress() => new(Name!.Trim(), Line1!.Trim(), null, City!.Trim(), PostalCode!.Trim(), Country!.Trim().ToUpperInvariant());
}

internal sealed record AdminPickupPointResponse(
    string Code,
    string Name,
    string Line1,
    string City,
    string PostalCode,
    string Country,
    bool IsActive);
