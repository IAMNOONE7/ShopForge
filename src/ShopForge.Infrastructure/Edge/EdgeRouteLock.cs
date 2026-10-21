using Npgsql;

namespace ShopForge.Infrastructure.Edge;

// One writer at a time, across every replica, for the route and the security policy all merchants' domains share
// (D-212). Neither carries an ETag, so two read-modify-writes crossing would each write the list it read and lose the
// other's domain. The lock is PostgreSQL's, taken in a transaction of its own connection, so a replica that dies while
// holding it lets go with its connection.
internal sealed class EdgeRouteLock(string connectionString)
{
    // Any fixed number nothing else in this database locks on: "SFEDGE" in ASCII.
    private const long Key = 0x534645444745;

    public async Task<IAsyncDisposable> HoldAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
            {
                command.Parameters.AddWithValue("key", Key);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return new Held(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }
    }

    private sealed class Held(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
