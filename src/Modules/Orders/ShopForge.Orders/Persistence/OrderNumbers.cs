using Microsoft.EntityFrameworkCore;

namespace ShopForge.Orders.Persistence;

internal static class OrderNumbers
{
    // One statement allocates the next number, so two checkouts in the same store can never take the same one.
    public static async Task<string> NextAsync(DbContext dbContext, Guid storeId, int year, CancellationToken cancellationToken)
    {
        var numbers = await dbContext.Database
            .SqlQueryRaw<int>(
                """
                INSERT INTO orders.order_numbers (store_id, year, next_number)
                VALUES ({0}, {1}, 1)
                ON CONFLICT (store_id, year) DO UPDATE SET next_number = orders.order_numbers.next_number + 1
                RETURNING next_number AS "Value"
                """,
                storeId,
                year)
            .ToListAsync(cancellationToken);

        return $"{year}-{numbers.Single():00000}";
    }
}
