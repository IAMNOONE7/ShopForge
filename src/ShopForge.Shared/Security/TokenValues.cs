using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ShopForge.Shared.Security;

// Links sent by e-mail — verify an address, reset a password, join a company — carry a value only the recipient
// has, and only its hash is kept, so a leaked row cannot be used to take over an account.
public static class TokenValues
{
    public static (string Value, string Hash) Create()
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        return (value, Hash(value));
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
