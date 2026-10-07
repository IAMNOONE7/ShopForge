using System.Security.Cryptography;
using System.Text;

namespace ShopForge.Shared.Security;

public static class Tokens
{
    // Compared without leaking how much of it was right. A feed token is a bearer credential in a URL, and
    // the only defence a long random string has is that guessing it tells an attacker nothing.
    public static bool Match(string offered, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(offered)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
