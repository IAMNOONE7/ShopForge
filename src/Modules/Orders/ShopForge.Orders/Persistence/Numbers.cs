using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Persistence;

internal static class Numbers
{
    // One statement allocates the next number, so two checkouts in the same store can never take the same one.
    public static async Task<int> NextAsync(DbContext dbContext, Guid storeId, string series, int year, CancellationToken cancellationToken)
    {
        var numbers = await dbContext.Database
            .SqlQueryRaw<int>(
                """
                INSERT INTO orders.number_sequences (store_id, series, year, next_number)
                VALUES ({0}, {1}, {2}, 1)
                ON CONFLICT (store_id, series, year) DO UPDATE SET next_number = orders.number_sequences.next_number + 1
                RETURNING next_number AS "Value"
                """,
                storeId,
                series,
                year)
            .ToListAsync(cancellationToken);

        return numbers.Single();
    }

    public static async Task<string> NextOrderNumberAsync(DbContext dbContext, Guid storeId, int year, CancellationToken cancellationToken) =>
        $"{year}-{await NextAsync(dbContext, storeId, NumberSeries.Order, year, cancellationToken):00000}";

    public static async Task<string> NextDocumentNumberAsync(
        DbContext dbContext,
        Guid storeId,
        string series,
        int year,
        CancellationToken cancellationToken)
    {
        var prefix = series == NumberSeries.CreditNote ? "CN" : "INV";

        return $"{prefix}-{year}-{await NextAsync(dbContext, storeId, series, year, cancellationToken):00000}";
    }
}
