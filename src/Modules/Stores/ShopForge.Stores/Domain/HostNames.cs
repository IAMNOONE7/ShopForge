using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace ShopForge.Stores.Domain;

internal static class HostNames
{
    // Stored domains and incoming Host headers share this normalization, so resolution is an exact match:
    // no port, no trailing dot, lower case, internationalized names in their ASCII (punycode) form.
    // STD3 rules are on, so anything that is not a legal host name (spaces, underscores, symbols) is rejected here
    // rather than by the database.
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
            return new IdnMapping { UseStd3AsciiRules = true }.GetAscii(hostName).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
