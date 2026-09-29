using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ShopForge.Shared.Security;

// The six digits from an authenticator app: RFC 6238, which is RFC 4226 counting thirty-second steps instead of
// button presses. Written out rather than taken from a package because it is thirty lines with official test
// vectors to check them against, and a dependency here would be a dependency inside sign-in (D-128).
public static class Totp
{
    public const int Digits = 6;

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    // A clock that is a step out is ordinary; a clock that is a minute out is somebody's problem to fix. One step
    // either side is the usual compromise.
    private const int Tolerance = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public static bool IsValid(string secret, string? code, DateTimeOffset now)
    {
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = FromBase32(secret);

        if (key.Length == 0)
        {
            return false;
        }

        var counter = now.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

        for (var drift = -Tolerance; drift <= Tolerance; drift++)
        {
            // Compared in fixed time: a code is a secret for thirty seconds, and how much of it matched is not
            // something to give away.
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Code(key, counter + drift)),
                    Encoding.ASCII.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }

    // What an authenticator app reads from a QR code. The issuer appears twice because that is what the apps
    // expect: once as the folder they file it under and once beside the account name.
    public static string EnrolmentUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}"
            + $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={(int)Step.TotalSeconds}";

    // The exact code for one moment, with no tolerance around it. Only the tests need this — verifying is the
    // job in production — but they need it precisely, or they end up asserting about a neighbouring step.
    internal static string CodeAt(string secret, DateTimeOffset moment) =>
        Code(FromBase32(secret), moment.ToUnixTimeSeconds() / (long)Step.TotalSeconds);

    internal static string Code(byte[] key, long counter)
    {
        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, counter);

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, message, hash);

        // The last nibble says where in the hash the number starts (RFC 4226 §5.3).
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        return (binary % 1_000_000).ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    private static string ToBase32(byte[] value)
    {
        var text = new StringBuilder();
        var bits = 0;
        var buffer = 0;

        foreach (var current in value)
        {
            buffer = (buffer << 8) | current;
            bits += 8;

            while (bits >= 5)
            {
                text.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return text.ToString();
    }

    private static byte[] FromBase32(string secret)
    {
        var bytes = new List<byte>();
        var bits = 0;
        var buffer = 0;

        foreach (var character in secret.TrimEnd('=').ToUpperInvariant())
        {
            var index = Base32Alphabet.IndexOf(character, StringComparison.Ordinal);

            if (index < 0)
            {
                return [];
            }

            buffer = (buffer << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
