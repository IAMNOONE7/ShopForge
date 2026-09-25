using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Platform;

namespace ShopForge.Catalog;

// How many products the company's plan covers, against how many it has. Catalog counts its own rows and asks Stores
// only for the cap (D-110).
internal static class Plans
{
    public static async Task<ProblemHttpResult?> RefusedAsync(
        DbContext dbContext,
        ITenantLimits limits,
        int adding,
        CancellationToken cancellationToken)
    {
        if (await limits.MaxAsync(TenantResource.Products, cancellationToken) is not { } maxProducts)
        {
            return null;
        }

        var products = await dbContext.Set<Product>().CountAsync(cancellationToken);

        if (products + adding <= maxProducts)
        {
            return null;
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This plan allows no more products",
            detail: $"The plan covers {maxProducts} products, the company has {products}, and this would add {adding}.");
    }
}
