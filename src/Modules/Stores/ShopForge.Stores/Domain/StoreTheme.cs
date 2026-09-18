using System.Text.RegularExpressions;

namespace ShopForge.Stores.Domain;

internal sealed partial record StoreTheme
{
    public StoreTheme(string primaryColor, string secondaryColor, int borderRadius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(borderRadius);

        PrimaryColor = ToHexColor(primaryColor, nameof(primaryColor));
        SecondaryColor = ToHexColor(secondaryColor, nameof(secondaryColor));
        BorderRadius = borderRadius;
    }

    public string PrimaryColor { get; private init; }

    public string SecondaryColor { get; private init; }

    public int BorderRadius { get; private init; }

    private static string ToHexColor(string value, string parameterName) =>
        HexColor().IsMatch(value)
            ? value.ToUpperInvariant()
            : throw new ArgumentException($"'{value}' is not a #RRGGBB color.", parameterName);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
