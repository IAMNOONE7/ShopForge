using System.Globalization;
using System.Text;

namespace ShopForge.Catalog.Search;

// Accents taken away, as `unaccent` takes them from the index and the query (D-224), for what is compared here rather
// than in the database. Decomposing covers every Czech and Slovak letter; the few letters Unicode does not decompose,
// such as ł and ø, stay as they are.
internal static class Folding
{
    public static string Of(string text)
    {
        var letters = text.Normalize(NormalizationForm.FormD)
            .Where(letter => CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark);

        return new string([.. letters]).Normalize(NormalizationForm.FormC);
    }
}
