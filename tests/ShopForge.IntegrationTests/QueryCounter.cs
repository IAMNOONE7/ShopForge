using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ShopForge.IntegrationTests;

// Counts the database round trips one request makes. A wall clock cannot see a query added per row — twenty-four
// indexed counts cost a few milliseconds on a machine with the database next door, and cost a page of the
// catalog on a machine that does not — so the baseline that fails the build counts queries instead (D-133).
//
// Only requests that ask to be counted are counted, and each names its own tally, so the rest of the suite can
// go on running in parallel around the measurement.
internal sealed class QueryCounter : DbCommandInterceptor
{
    public const string HeaderName = "X-ShopForge-Count-Queries";

    private static readonly HttpContextAccessor Requests = new();

    private readonly ConcurrentDictionary<string, int> _counts = new();

    public static string NewTally() => Guid.NewGuid().ToString("N");

    public int this[string tally] => _counts.GetValueOrDefault(tally);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Count();

        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Count();

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Count();

        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Count();

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Count();

        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Count();

        return ValueTask.FromResult(result);
    }

    private void Count()
    {
        if (Requests.HttpContext?.Request.Headers[HeaderName].ToString() is { Length: > 0 } tally)
        {
            _counts.AddOrUpdate(tally, 1, (_, counted) => counted + 1);
        }
    }
}
