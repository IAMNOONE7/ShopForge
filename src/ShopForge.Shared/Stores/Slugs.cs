using System.Globalization;
using System.Text;

namespace ShopForge.Shared.Stores;

// What a slug may contain, beside the shapes that put one in an address (D-149): a page's name in a URL is one
// contract, and every module that gives something a name needs the same answer about it (D-175).
public static class Slugs
{
    public const int MaxLength = 120;

    public static string Create(string text)
    {
        var slug = new StringBuilder(text.Length);

        foreach (var character in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(character);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().TrimEnd('-');

        return result.Length <= MaxLength ? result : result[..MaxLength].TrimEnd('-');
    }

    public static bool IsValid(string slug) => slug.Length is > 0 and <= MaxLength && Create(slug) == slug;
}
