using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ShopForge.Infrastructure.Caching;

// Holds one connection open on the channel and forgets whatever it names. A connection that breaks makes the
// replica deaf until another is in place — keeping nothing, rather than keeping what it can no longer be told
// about — and is replaced after a pause that grows to half a minute (D-202).
internal sealed class CacheListener(CacheChannel channel, ReplicaCache cache, TimeProvider clock, ILogger<CacheListener> logger) : BackgroundService
{
    private const string Listen = "LISTEN " + CacheChannel.Name;

    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstPause = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestPause = TimeSpan.FromSeconds(30);

    private TimeSpan _pause = FirstPause;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ListenAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    cache.Deaf();
                    logger.LogWarning(exception, "The cache channel was lost; this replica keeps nothing until it is back.");
                    await Task.Delay(_pause, clock, stoppingToken);
                    _pause = _pause * 2 < LongestPause ? _pause * 2 : LongestPause;
                }
            }
        }
        finally
        {
            cache.Deaf();
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await using var connection = await channel.OpenListenerAsync(stoppingToken);
        connection.Notification += (_, notification) => cache.Forget([notification.Payload]);

        await using (var listen = new NpgsqlCommand(Listen, connection))
        {
            await listen.ExecuteNonQueryAsync(stoppingToken);
        }

        // Only from here on can a change be heard, so only from here on is anything kept.
        cache.Hearing();
        _pause = FirstPause;
        logger.LogInformation("Listening for cache changes as {Listener}.", channel.ListenerName);

        while (!stoppingToken.IsCancellationRequested)
        {
            // A connection that has quietly died answers nothing, so a long silence is checked rather than trusted.
            if (!await connection.WaitAsync(Quiet, stoppingToken))
            {
                await using var ping = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = 10 };
                await ping.ExecuteScalarAsync(stoppingToken);
            }
        }
    }
}
