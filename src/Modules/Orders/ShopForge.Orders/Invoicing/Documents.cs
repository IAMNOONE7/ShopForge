using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Storefront;

internal static class Documents
{
    public static async Task<List<DocumentResponse>> OfAsync(DbContext dbContext, string orderNumber, CancellationToken cancellationToken) =>
        await dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(invoice => invoice.OrderNumber == orderNumber)
            .OrderBy(invoice => invoice.IssuedAt)
            .Select(invoice => new DocumentResponse(invoice.Number, invoice.Kind.ToString(), invoice.IssuedAt))
            .ToListAsync(cancellationToken);
}

internal sealed record DocumentResponse(string Number, string Kind, DateTimeOffset IssuedAt);
