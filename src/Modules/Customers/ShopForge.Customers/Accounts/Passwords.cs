using Microsoft.AspNetCore.Identity;
using ShopForge.Customers.Domain;

namespace ShopForge.Customers.Accounts;

internal static class Passwords
{
    // Hashing even for unknown e-mails keeps the response time from revealing which accounts exist.
    public static PasswordVerificationResult VerifyAgainstDummyHash(IPasswordHasher<CustomerIdentity> passwordHasher, string? password)
    {
        passwordHasher.VerifyHashedPassword(null!, DummyHash.Value, password ?? "");

        return PasswordVerificationResult.Failed;
    }

    private static class DummyHash
    {
        public static readonly string Value = new PasswordHasher<CustomerIdentity>().HashPassword(null!, Guid.NewGuid().ToString());
    }
}
