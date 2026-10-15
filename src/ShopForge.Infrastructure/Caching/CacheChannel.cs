using Microsoft.Extensions.Logging;
using Npgsql;

namespace ShopForge.Infrastructure.Caching;

// The word every replica listens for, carried by the database every replica already shares: PostgreSQL's
// LISTEN/NOTIFY, which needs no broker and delivers a notification to every session listening when it is sent
// (D-202). A notification names one cache key.
internal sealed class CacheChannel : IAsyncDisposable
{
    public const string Name = "shopforge_cache";

    private readonly NpgsqlDataSource _speaking;
    private readonly NpgsqlDataSource _listening;
    private readonly ILogger<CacheChannel> _logger;

    public CacheChannel(string connectionString, ILogger<CacheChannel> logger)
    {
        _logger = logger;
        _speaking = NpgsqlDataSource.Create(connectionString);

        // Named, so an operator can see each replica's listener in pg_stat_activity and a test can cut one off.
        // Unpooled, so a connection that has gone bad is closed rather than handed back. Its keep-alive query
        // runs every thirty seconds on every replica and says nothing anybody needs in a trace.
        ListenerName = $"shopforge-cache-{Guid.NewGuid():N}";
        var listener = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = ListenerName, Pooling = false };
        var builder = new NpgsqlDataSourceBuilder(listener.ConnectionString);
        builder.ConfigureTracing(tracing => tracing.ConfigureCommandFilter(_ => false));
        _listening = builder.Build();
    }

    public string ListenerName { get; }

    public ValueTask<NpgsqlConnection> OpenListenerAsync(CancellationToken cancellationToken) =>
        _listening.OpenConnectionAsync(cancellationToken);

    // After the change is saved, and never tied to the request that saved it: a shopper closing the tab must not
    // stop the other replicas hearing that a store has gone. A failure is logged rather than thrown, because the
    // change itself stands; the replicas that missed the word keep their copy until it expires.
    public async Task SayAsync(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        try
        {
            await using var command = _speaking.CreateCommand("SELECT pg_notify(@channel, key) FROM unnest(@keys) AS key");
            command.Parameters.AddWithValue("channel", Name);
            command.Parameters.AddWithValue("keys", keys.ToArray());
            command.CommandTimeout = 10;
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            _logger.LogError(exception, "The other replicas could not be told to forget {Count} cached entries; they keep them until they expire.", keys.Count);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _speaking.DisposeAsync();
        await _listening.DisposeAsync();
    }
}
