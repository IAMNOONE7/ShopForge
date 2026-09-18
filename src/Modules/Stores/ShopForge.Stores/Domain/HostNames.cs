using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace ShopForge.Stores.Domain;

internal static class HostNames
{
    // Stored domains and incoming Host headers share this normalization, so resolution is an exact match:
    // no port, no trailing dot, lower case, internationalized names in their ASCII (punycode) form.
    public static string? Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var hostName = new HostString(host.Trim()).Host.TrimEnd('.');

        if (hostName.Length == 0)
        {
            return null;
        }

        try
        {
            return new IdnMapping().GetAscii(hostName).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
