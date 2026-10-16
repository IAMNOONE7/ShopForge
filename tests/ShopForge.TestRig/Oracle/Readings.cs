using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using ShopForge.TestRig.Runs;

namespace ShopForge.TestRig.Oracle;

// What a run left in the database, read through a session the server itself holds to read-only, and only ever for the
// run's own stores and company: a journey asserts the end state, not only the page, and cannot change it doing so
// (D-203).
internal sealed class Readings(RigOptions options) : IAsyncDisposable
{
    private readonly NpgsqlDataSource _database = NpgsqlDataSource.Create(options.Database);

    public string DatabaseName { get; } = new NpgsqlConnectionStringBuilder(options.Database).Database ?? string.Empty;

    public async Task<OrderReading?> OrderAsync(Guid storeId, string number, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        var order = await SingleAsync(
            connection,
            """
            SELECT number, status, email, currency, placed_at, paid_at, payment_provider_key, payment_reference, refunded_total
            FROM orders.orders WHERE store_id = @store AND number = @number
            """,
            storeId,
            number,
            reader => new OrderFacts(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5), Text(reader, 6), Text(reader, 7), reader.GetDecimal(8)),
            cancellationToken);

        if (order is null)
        {
            return null;
        }

        return new OrderReading(
            order,
            await ListAsync(
                connection,
                """
                SELECT line.product_name, line.variant_id, line.quantity, line.unit_price, line.discount
                FROM orders.order_lines AS line JOIN orders.orders AS o ON o.id = line.order_id
                WHERE o.store_id = @store AND o.number = @number ORDER BY line.product_name
                """,
                storeId,
                number,
                reader => new LineReading(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetInt32(2), reader.GetDecimal(3), reader.GetDecimal(4)),
                cancellationToken),
            await ListAsync(
                connection,
                """
                SELECT provider, reference, status, amount, currency, environment
                FROM orders.payment_attempts WHERE store_id = @store AND order_number = @number ORDER BY started_at
                """,
                storeId,
                number,
                reader => new AttemptReading(reader.GetString(0), Text(reader, 1), reader.GetString(2), reader.GetDecimal(3), reader.GetString(4), Text(reader, 5)),
                cancellationToken),
            (await ListAsync(
                connection,
                "SELECT count(*)::int FROM orders.payment_events WHERE store_id = @store AND order_number = @number",
                storeId,
                number,
                reader => reader.GetInt32(0),
                cancellationToken)).Single(),
            await ListAsync(
                connection,
                """
                SELECT provider, reference, amount, currency, reason, status
                FROM orders.unapplied_payments WHERE store_id = @store AND order_number = @number ORDER BY received_at
                """,
                storeId,
                number,
                reader => new KeptReading(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)),
                cancellationToken),
            await ListAsync(
                connection,
                """
                SELECT number, status, refund_status, refunded_amount, refund_problem
                FROM orders.order_returns WHERE store_id = @store AND order_number = @number ORDER BY requested_at
                """,
                storeId,
                number,
                reader => new ReturnReading(reader.GetString(0), reader.GetString(1), Text(reader, 2), reader.IsDBNull(3) ? null : reader.GetDecimal(3), Text(reader, 4)),
                cancellationToken),
            await ListAsync(
                connection,
                "SELECT number, kind FROM orders.invoices WHERE store_id = @store AND order_number = @number ORDER BY issued_at",
                storeId,
                number,
                reader => new DocumentReading(reader.GetString(0), reader.GetString(1)),
                cancellationToken),
            await ListAsync(
                connection,
                """
                SELECT type, status FROM messaging.outbox_messages
                WHERE store_id = @store AND payload LIKE '%"' || @number || '"%' ORDER BY created_at
                """,
                storeId,
                number,
                reader => new LetterReading(reader.GetString(0), reader.GetString(1)),
                cancellationToken));
    }

    // Stock is the company's, shared by both of a run's stores; a code is looked up only among the run company's own.
    public async Task<StockReading?> StockAsync(Guid tenantId, string sku, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT variant.sku, coalesce(sum(item.quantity_on_hand), 0)::int, coalesce(sum(item.quantity_reserved), 0)::int
            FROM catalog.product_variants AS variant
            LEFT JOIN inventory.inventory_items AS item ON item.variant_id = variant.id
            WHERE variant.tenant_id = @tenant AND variant.sku = @sku
            GROUP BY variant.sku
            """,
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("sku", sku.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new StockReading(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(1) - reader.GetInt32(2))
            : null;
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private static async Task<T?> SingleAsync<T>(
        NpgsqlConnection connection, string sql, Guid storeId, string number, Func<NpgsqlDataReader, T> read, CancellationToken cancellationToken)
        where T : class =>
        (await ListAsync(connection, sql, storeId, number, read, cancellationToken)).SingleOrDefault();

    private static async Task<List<T>> ListAsync<T>(
        NpgsqlConnection connection, string sql, Guid storeId, string number, Func<NpgsqlDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("number", number);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private static string? Text(NpgsqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
}

internal sealed record OrderReading(
    OrderFacts Order,
    List<LineReading> Lines,
    List<AttemptReading> Attempts,
    int AnswersRecorded,
    List<KeptReading> Kept,
    List<ReturnReading> Returns,
    List<DocumentReading> Documents,
    List<LetterReading> Letters);

internal sealed record OrderFacts(
    string Number,
    string Status,
    string Email,
    string Currency,
    DateTimeOffset PlacedAt,
    DateTimeOffset? PaidAt,
    string? PaymentProvider,
    string? PaymentReference,
    decimal RefundedTotal);

internal sealed record LineReading(string ProductName, Guid? VariantId, int Quantity, decimal UnitPrice, decimal Discount);

internal sealed record AttemptReading(string Provider, string? Reference, string Status, decimal Amount, string Currency, string? Environment);

internal sealed record KeptReading(string Provider, string Reference, decimal Amount, string Currency, string Reason, string Status);

internal sealed record ReturnReading(string Number, string Status, string? RefundStatus, decimal? RefundedAmount, string? RefundProblem);

internal sealed record DocumentReading(string Number, string Kind);

internal sealed record LetterReading(string Type, string Status);

internal sealed record StockReading(string Sku, int OnHand, int Reserved, int Available);

internal static class ReadingEndpoints
{
    public static void MapReadings(this WebApplication app)
    {
        app.MapGet("/runs/{runId}/stores/{storeId:guid}/orders/{number}", OrderAsync);
        app.MapGet("/runs/{runId}/stock/{sku}", StockAsync);
    }

    private static async Task<Results<Ok<OrderReading>, NotFound>> OrderAsync(
        string runId, Guid storeId, string number, RunBook runs, Readings readings, CancellationToken cancellationToken) =>
        runs.Find(runId) is { } run && run.Owns(storeId) && await readings.OrderAsync(storeId, number, cancellationToken) is { } order
            ? TypedResults.Ok(order)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<StockReading>, NotFound>> StockAsync(
        string runId, string sku, RunBook runs, Readings readings, CancellationToken cancellationToken) =>
        runs.Find(runId) is { } run && await readings.StockAsync(run.TenantId, sku, cancellationToken) is { } stock
            ? TypedResults.Ok(stock)
            : TypedResults.NotFound();
}
