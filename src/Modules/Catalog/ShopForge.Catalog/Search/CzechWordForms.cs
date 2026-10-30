namespace ShopForge.Catalog.Search;

// How a Czech word typed into a search finds the other forms of it: `šroubováky`, `šroubováků` and `šroubovákem` are
// all `šroubovák` (D-225). PostgreSQL has no Czech stemmer and a managed server cannot be given one, so the query does
// the work and the index keeps whole words.
//
// The endings are those of the light stemmer by Dolamic and Savoy ("Indexing and stemming approaches for the Czech
// language", 2009), as Apache Lucene implements it, each also in its form without accents, because a shopper may type
// either. Only endings are removed: the index holds whole words, and a stem that was rewritten would be the prefix of
// none of them. Lucene's last step, which rewrites `klíč` to `klík`, is left out for that reason, and two of its
// alternations are kept as extra prefixes instead: the mobile e (`šroubek`, `šroubky`) and ů for o (`stůl`, `stoly`).
internal static class CzechWordForms
{
    // A stem shorter than this matches too much to mean anything. A shorter word is used as typed, and the lengths an
    // ending may be removed at keep every stem at three letters or more.
    private const int ShortestStem = 3;

    private static readonly string[][] CaseEndings =
    [
        WithoutAccents("atech"),
        WithoutAccents("ětem", "etem", "atům"),
        WithoutAccents(
            "ech", "ich", "ích", "ého", "ěmi", "emi", "ému", "ěte", "ete", "ěti", "eti", "ího", "iho", "ími", "ímu", "imu",
            "ách", "ata", "aty", "ých", "ama", "ami", "ové", "ovi", "ými"),
        WithoutAccents("em", "es", "ém", "ím", "ům", "at", "ám", "os", "us", "ým", "mi", "ou"),
    ];

    // Removed only from a word longer than these, longest ending first.
    private static readonly int[] LongerThan = [7, 6, 5, 4];

    private static readonly string[] PossessiveEndings = WithoutAccents("ov", "in", "ův");

    private const string OneLetterEndings = "aeiouůyáéíýě";

    private const string Vowels = "aáeéěiíoóuúůyý";

    // The consonants a mobile e stands before at the end of a stem: `šroubek`, `konec`, `kotel`, `kmen`, `vítr` is not.
    private const string AfterMobileE = "kclnr";

    // Every ending a stem can be followed by, without accents: what a form of the word is, as the index holds it.
    public static IReadOnlyList<string> Endings { get; } =
    [
        "", .. CaseEndings.SelectMany(endings => endings).Concat(PossessiveEndings).Concat(OneLetterEndings.Select(letter => letter.ToString()))
            .Select(Folding.Of).Distinct().OrderBy(ending => ending.Length).ThenBy(ending => ending, StringComparer.Ordinal),
    ];

    // The beginnings that find a word's forms: its stem, then the stem with the alternation undone. The word arrives
    // in lower case, accents as typed.
    public static IReadOnlyList<string> PrefixesOf(string word)
    {
        if (word.Length < ShortestStem)
        {
            return [word];
        }

        var stem = WithoutPossessive(WithoutCase(word));
        var prefixes = new List<string> { stem };

        if (stem.Length > ShortestStem && stem[^2] == 'e' && AfterMobileE.Contains(stem[^1]))
        {
            prefixes.Add(stem[..^2] + stem[^1]);
        }
        else if (stem.Length > ShortestStem && AfterMobileE.Contains(stem[^1]) && !IsVowel(stem[^2]))
        {
            prefixes.Add(stem[..^1] + "e" + stem[^1]);
        }

        // A plain u can be a ů typed without its ring, but only in a word typed without any accent at all.
        if (stem[^2] == 'ů' || (stem[^2] == 'u' && Folding.Of(word) == word))
        {
            prefixes.Add(stem[..^2] + "o" + stem[^1]);
        }
        else if (stem[^2] == 'o')
        {
            prefixes.Add(stem[..^2] + "ů" + stem[^1]);
        }

        return prefixes;
    }

    private static string WithoutCase(string word)
    {
        for (var index = 0; index < CaseEndings.Length; index++)
        {
            if (word.Length > LongerThan[index] && CaseEndings[index].FirstOrDefault(candidate => word.EndsWith(candidate, StringComparison.Ordinal)) is { } ending)
            {
                return word[..^ending.Length];
            }
        }

        return word.Length > ShortestStem && OneLetterEndings.Contains(word[^1]) ? word[..^1] : word;
    }

    private static string WithoutPossessive(string stem) =>
        stem.Length > 5 && PossessiveEndings.FirstOrDefault(candidate => stem.EndsWith(candidate, StringComparison.Ordinal)) is { } ending ? stem[..^ending.Length] : stem;

    private static bool IsVowel(char letter) => Vowels.Contains(letter);

    private static string[] WithoutAccents(params string[] endings) =>
        [.. endings.Concat(endings.Select(Folding.Of)).Distinct()];
}
