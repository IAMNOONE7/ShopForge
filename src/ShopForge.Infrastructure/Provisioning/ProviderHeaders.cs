using System.Globalization;

namespace ShopForge.Infrastructure.Provisioning;

// How long a provider asked to be left alone, read from its own headers rather than guessed (D-212). Azure Resource
// Manager and Cloudflare both send Retry-After with a 429, in seconds or as a date. Cloudflare's IETF RateLimit header,
// `"default";r=0;t=30`, says when a spent quota comes back, and is read when Retry-After is missing.
internal static class ProviderHeaders
{
    public static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            return retryAfter.Delta is { } delta ? Positive(delta)
                : retryAfter.Date is { } date ? Positive(date - now)
                : null;
        }

        return response.Headers.TryGetValues("Ratelimit", out var values) ? Reset(values) : null;
    }

    // The longest wait of the quotas that are spent, or of all of them when none says it is.
    private static TimeSpan? Reset(IEnumerable<string> values)
    {
        var items = values
            .SelectMany(value => value.Split(','))
            .Select(item => (Remaining: Parameter(item, "r"), Reset: Parameter(item, "t")))
            .Where(item => item.Reset is not null)
            .ToList();
        var spent = items.Where(item => item.Remaining == 0).ToList();
        var longest = (spent.Count > 0 ? spent : items).Max(item => item.Reset);

        return longest is { } seconds ? Positive(TimeSpan.FromSeconds(seconds)) : null;
    }

    private static int? Parameter(string item, string name) =>
        item.Split(';')
            .Select(part => part.Trim())
            .Where(part => part.StartsWith($"{name}=", StringComparison.Ordinal))
            .Select(part => int.TryParse(part[(name.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : (int?)null)
            .FirstOrDefault();

    private static TimeSpan? Positive(TimeSpan wait) => wait > TimeSpan.Zero ? wait : null;
}
