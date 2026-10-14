using System.Diagnostics;

namespace ShopForge.Infrastructure.Diagnostics;

// An outgoing call's span records the address it called as `url.full`, and the exporter sends it on. The
// instrumentation redacts a query string, never a path, and Packeta puts its API password in the path. A request that
// says its address is a credential keeps its host in the trace and nothing after it, so the call is still timed and
// still found (D-199).
internal static class TelemetryRedaction
{
    public static readonly HttpRequestOptionsKey<bool> AddressIsACredential = new("ShopForge.AddressIsACredential");

    public static void Redact(Activity activity, HttpRequestMessage request)
    {
        if (request.Options.TryGetValue(AddressIsACredential, out var credential) && credential && request.RequestUri is { } address)
        {
            activity.SetTag("url.full", $"{address.Scheme}://{address.Authority}/[redacted]");
        }
    }
}
