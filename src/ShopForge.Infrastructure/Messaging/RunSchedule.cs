using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ShopForge.Infrastructure.Messaging;

// The claim behind every scheduled pass. A pass seen for the first time is due one period from now, as a timer
// started at boot would have made it; after that the database keeps the time, so a restart does not move it.
// Claiming is moving the time on: the replica whose update finds the pass due takes it, and every other replica
// asking at the same moment waits on that row and then finds it not due (D-202). Time is the database's, so
// replicas whose clocks disagree still agree on what is due.
internal sealed class RunSchedule(IServiceScopeFactory scopes)
{
    // Who took it, for whoever is reading the table: the container's host name is the replica's name.
    public static readonly string Replica = $"{Environment.MachineName}/{Environment.ProcessId}";

    public async Task<bool> ClaimAsync(string name, TimeSpan period, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<DbContext>().Database;

        await database.ExecuteSqlAsync(
            $"""
            INSERT INTO messaging.scheduled_runs (name, due_at)
            VALUES ({name}, now() + {period})
            ON CONFLICT (name) DO NOTHING
            """,
            cancellationToken);

        var claimed = await database.ExecuteSqlAsync(
            $"""
            UPDATE messaging.scheduled_runs
            SET due_at = now() + {period}, claimed_by = {Replica}, claimed_at = now()
            WHERE name = {name} AND due_at <= now()
            """,
            cancellationToken);

        return claimed == 1;
    }
}
