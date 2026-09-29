using Microsoft.AspNetCore.DataProtection;

namespace ShopForge.Shared.Security;

// What carries a half-finished sign-in between the password and the code. Protected rather than stored: a row
// for something that lives five minutes is a table to sweep, and the key ring already exists (D-128).
//
// The purpose keeps the kinds of sign-in apart. A company's staff and the platform's operators are separate
// authentication domains (D-103), and a ticket earned in one must be unreadable in the other — which a different
// purpose guarantees by the key, not by a check somebody could forget (D-129).
public static class TwoFactorTickets
{
    public const string TenantUser = "ShopForge.TwoFactor.TenantUser";

    public const string PlatformUser = "ShopForge.TwoFactor.PlatformUser";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static string Issue(IDataProtectionProvider dataProtection, string purpose, Guid userId, Guid securityStamp, DateTimeOffset now) =>
        Protector(dataProtection, purpose).Protect($"{userId:N}:{securityStamp:N}", now + Lifetime);

    public static PendingSignIn? Read(IDataProtectionProvider dataProtection, string purpose, string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return null;
        }

        try
        {
            var parts = Protector(dataProtection, purpose).Unprotect(ticket).Split(':');

            return parts is [var user, var stamp]
                && Guid.TryParseExact(user, "N", out var userId)
                && Guid.TryParseExact(stamp, "N", out var securityStamp)
                    ? new PendingSignIn(userId, securityStamp)
                    : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Expired, tampered with, meant for the other kind of sign-in, or from a key ring that has since
            // been replaced. All of them mean no.
            return null;
        }
    }

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider dataProtection, string purpose) =>
        dataProtection.CreateProtector(purpose).ToTimeLimitedDataProtector();
}

public sealed record PendingSignIn(Guid UserId, Guid SecurityStamp);
