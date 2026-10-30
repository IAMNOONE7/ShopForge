namespace ShopForge.Catalog.Domain;

// Words a shopper might type that a listing's or a category's own text does not hold: another name, a shortcut, a
// common misspelling, the English word. Found as the name is found, and ranked below it (D-226).
internal static class Keywords
{
    public const int MaxLength = 500;

    public static bool Fit(string? keywords) => keywords is null || keywords.Trim().Length <= MaxLength;

    // Blank is none. The rest is kept as typed, commas and line breaks included: the search splits it into words.
    public static string? Tidied(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            return null;
        }

        var tidied = keywords.Trim();

        return tidied.Length <= MaxLength
            ? tidied
            : throw new ArgumentException($"Search keywords can be up to {MaxLength} characters.", nameof(keywords));
    }
}
