using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace ShopForge.Access.Authentication;

// What carries a half-finished sign-in between the password and the code. Protected rather than stored: a row
// for something that lives five minutes is a table to sweep, and the key ring already exists (D-128).
internal static class TwoFactorTickets
{
    private const string Purpose = "ShopForge.Access.TwoFactor";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static string Issue(IDataProtectionProvider dataProtection, Guid userId, Guid securityStamp, DateTimeOffset now) =>
        dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector()
            .Protect($"{userId:N}:{securityStamp:N}", now + Lifetime);

    public static PendingSignIn? Read(IDataProtectionProvider dataProtection, string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return null;
        }

        try
        {
            var parts = dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector().Unprotect(ticket).Split(':');

            return parts is [var user, var stamp]
                && Guid.TryParseExact(user, "N", out var userId)
                && Guid.TryParseExact(stamp, "N", out var securityStamp)
                    ? new PendingSignIn(userId, securityStamp)
                    : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Expired, tampered with, or from a key ring that has since been replaced. All of them mean no.
            return null;
        }
    }
}

internal sealed record PendingSignIn(Guid UserId, Guid SecurityStamp);
