using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Inventory.Domain;
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
        stock.MapGet("/{productId:guid}/movements", GetMovementsAsync);
        stock.MapPut("/{productId:guid}", SetStockAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
    }

    private static async Task<Ok<List<StockResponse>>> GetStockAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<InventoryItem>()
            .OrderBy(item => item.ProductId)
            .Select(item => new StockResponse(item.ProductId, item.QuantityOnHand, item.QuantityReserved, item.QuantityOnHand - item.QuantityReserved))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Ok<List<StockMovementResponse>>, NotFound>> GetMovementsAsync(
        Guid productId,
        DbContext dbContext,
        ITenantProducts products,
        CancellationToken cancellationToken)
    {
        // The movements themselves are the company's and nothing of another's could be read here, but answering
        // "here are none" for a product the caller cannot see is not an answer it should get (D-127).
        if (!await products.ExistsAsync(productId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await dbContext.Set<StockMovement>()
            .Where(movement => movement.ProductId == productId)
            .OrderByDescending(movement => movement.OccurredAt)
            .Take(50)
            .Select(movement => new StockMovementResponse(movement.OccurredAt, movement.Quantity, movement.Reason.ToString(), movement.Reference))
            .ToListAsync(cancellationToken));
    }

    private static async Task<Results<Ok<StockResponse>, ValidationProblem, NotFound, ProblemHttpResult>> SetStockAsync(
        Guid productId,
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

        if (!await products.ExistsAsync(productId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (!await stock.SetOnHandAsync(productId, request.Quantity, "manual", cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The quantity is lower than what orders have reserved",
                detail: "Cancel or fulfil the open orders for this product first.");
        }

        audit.Record("stock.set", productId.ToString(), new { request.Quantity });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var item = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync(candidate => candidate.ProductId == productId, cancellationToken);

        return TypedResults.Ok(new StockResponse(productId, item.QuantityOnHand, item.QuantityReserved, item.QuantityAvailable));
    }
}

internal sealed record SetStockRequest(int Quantity);

internal sealed record StockResponse(Guid ProductId, int OnHand, int Reserved, int Available);

internal sealed record StockMovementResponse(DateTimeOffset OccurredAt, int Quantity, string Reason, string Reference);
