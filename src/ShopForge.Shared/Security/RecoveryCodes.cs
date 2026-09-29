using System.Security.Cryptography;

namespace ShopForge.Shared.Security;

public static class RecoveryCodes
{
    public const int Count = 10;

    // Grouped and short enough to be written on paper and typed back without a mistake, long enough that
    // guessing one is not worth trying: ten codes of forty bits each.
    public static IReadOnlyList<string> Issue() =>
        [.. Enumerable.Range(0, Count).Select(_ => $"{Group()}-{Group()}")];

    private static string Group() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3)).ToUpperInvariant();
}
