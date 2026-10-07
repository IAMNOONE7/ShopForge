using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Inventory.Domain;
using ShopForge.Shared.Admin;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Http;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Security;

namespace ShopForge.Inventory.Admin;

internal static class AdminStockEndpoints
{
    public static void MapAdminStock(this IEndpointRouteBuilder tenantAdmin)
    {
        var stock = tenantAdmin.MapGroup("/stock");

        stock.MapGet("/", GetStockAsync);
        stock.MapGet("/{variantId:guid}/movements", GetMovementsAsync);
        stock.MapPut("/{variantId:guid}", SetStockAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
    }

    private static async Task<Ok<List<StockResponse>>> GetStockAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<InventoryItem>()
            .OrderBy(item => item.VariantId)
            .Select(item => new StockResponse(item.VariantId, item.QuantityOnHand, item.QuantityReserved, item.QuantityOnHand - item.QuantityReserved))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Ok<AdminListResponse<StockMovementResponse>>, NotFound>> GetMovementsAsync(
        Guid variantId,
        DbContext dbContext,
        ITenantProducts products,
        int? page,
        int? pageSize,
        string? sort,
        CancellationToken cancellationToken)
    {
        // The movements themselves are the company's and nothing of another's could be read here, but answering
        // "here are none" for something the caller cannot see is not an answer it should get (D-127).
        if (!await products.VariantExistsAsync(variantId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        // A year of movements for a thing that sells is a list to walk, not fifty rows and silence about the
        // rest (D-179). Newest first, because an adjustment is usually about something that just happened.
        var asked = AdminListQuery.Of(page, pageSize, sort, terms: null);
        var movements = dbContext.Set<StockMovement>()
            .Where(movement => movement.VariantId == variantId)
            .OrderByDescending(movement => movement.OccurredAt)
            .ThenBy(movement => movement.Id);

        var total = await movements.CountAsync(cancellationToken);
        var wanted = await movements
            .Skip(asked.Skipped)
            .Take(asked.Taken)
            .Select(movement => new StockMovementResponse(movement.OccurredAt, movement.Quantity, movement.Reason.ToString(), movement.Reference))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AdminListResponse<StockMovementResponse>(wanted, total, asked.Page, asked.PageSize));
    }

    private static async Task<Results<Ok<StockResponse>, ValidationProblem, NotFound, ProblemHttpResult>> SetStockAsync(
        Guid variantId,
        SetStockRequest request,
        DbContext dbContext,
        IStockLedger stock,
        ITenantProducts products,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors().Check(request.Quantity >= 0, "quantity", "Quantity cannot be negative.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (!await products.VariantExistsAsync(variantId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (!await stock.SetOnHandAsync(variantId, request.Quantity, "manual", cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The quantity is lower than what orders have reserved",
                detail: "Cancel or fulfil the open orders for this product first.");
        }

        audit.Record("stock.set", variantId.ToString(), new { request.Quantity });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var item = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync(candidate => candidate.VariantId == variantId, cancellationToken);

        return TypedResults.Ok(new StockResponse(variantId, item.QuantityOnHand, item.QuantityReserved, item.QuantityAvailable));
    }
}

internal sealed record SetStockRequest(int Quantity);

internal sealed record StockResponse(Guid VariantId, int OnHand, int Reserved, int Available);

internal sealed record StockMovementResponse(DateTimeOffset OccurredAt, int Quantity, string Reason, string Reference);
