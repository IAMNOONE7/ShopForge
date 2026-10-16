using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ShopForge.Infrastructure.Persistence;

// Whether the database has every migration this build was made with. A release migrates before its new revision
// serves (D-205); this is what still holds when the order goes wrong: a build whose schema has not caught up answers
// nothing but its health, says why it is not ready, and keeps its background work waiting. Once caught up it is not
// asked again. A schema does not go backwards under a running build, and a newer one, which a rollback runs the
// previous build against, is not behind.
public sealed class SchemaGate(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SchemaGate> logger)
{
    private static readonly TimeSpan BetweenAsks = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _asking = new(1, 1);
    private volatile bool _caughtUp;
    private string? _reported;

    public bool IsCaughtUp => _caughtUp;

    // The migrations this build carries and the database has not applied, oldest first; none once it has caught up.
    public async Task<IReadOnlyList<string>> PendingAsync(CancellationToken cancellationToken)
    {
        if (_caughtUp)
        {
            return [];
        }

        await _asking.WaitAsync(cancellationToken);

        try
        {
            if (_caughtUp)
            {
                return [];
            }

            await using var scope = scopes.CreateAsyncScope();
            var pending = (await scope.ServiceProvider.GetRequiredService<ShopForgeDbContext>().Database
                .GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pending.Count == 0)
            {
                _caughtUp = true;

                if (_reported is not null)
                {
                    logger.LogInformation("The database has caught up with this build, which serves from now on.");
                }
            }
            else if (_reported != pending[0])
            {
                _reported = pending[0];
                logger.LogWarning(
                    "The database is {Count} migration(s) behind this build, starting with {Migration}; the build serves nothing until they are applied.",
                    pending.Count,
                    pending[0]);
            }

            return pending;
        }
        finally
        {
            _asking.Release();
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                if ((await PendingAsync(cancellationToken)).Count == 0)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "The database could not say which migrations it has; asking again shortly.");
            }

            await Task.Delay(BetweenAsks, clock, cancellationToken);
        }
    }
}
