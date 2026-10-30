using System.Text;

namespace ShopForge.Catalog.Search;

// What somebody typed, read the way `websearch_to_tsquery` read it before (D-178): bare words, "a phrase" and
// -excluded words, plus codes told apart from words, because a code is matched as typed and a word by its forms
// (D-225). Only letters and digits survive: everything else is a separator, so nothing typed can reach the query's
// own syntax.
internal static class SearchTokens
{
    // A pasted paragraph is not a search for each of its words.
    public const int MostTokens = 10;

    public static IReadOnlyList<SearchToken> Read(string typed)
    {
        var tokens = new List<SearchToken>();
        var runes = typed.EnumerateRunes().ToArray();
        var index = 0;

        while (index < runes.Length && tokens.Count < MostTokens)
        {
            var rune = runes[index];

            if (rune.Value == '"')
            {
                var close = Array.FindIndex(runes, index + 1, candidate => candidate.Value == '"');
                var end = close < 0 ? runes.Length : close;
                var words = WordsIn(runes, index + 1, end).Select(word => word.Lowered).ToList();

                if (words.Count > 0)
                {
                    tokens.Add(new SearchToken(SearchTokenKind.Phrase, words));
                }

                index = end + 1;
            }
            else if (rune.Value == '-' && (index == 0 || Rune.IsWhiteSpace(runes[index - 1])) && index + 1 < runes.Length && IsWordRune(runes[index + 1]))
            {
                var (word, next) = WordAt(runes, index + 1);
                tokens.Add(new SearchToken(SearchTokenKind.Excluded, [word.Lowered]));
                index = next;
            }
            else if (IsWordRune(rune))
            {
                var (word, next) = WordAt(runes, index);
                tokens.Add(new SearchToken(word.IsCode ? SearchTokenKind.Code : SearchTokenKind.Word, [word.Lowered]));
                index = next;
            }
            else
            {
                index++;
            }
        }

        // A single letter matches half a catalogue, so it is dropped from a search with anything else in it.
        var meaningful = tokens.Where(token => token.Kind != SearchTokenKind.Word || token.Text.Length > 1).ToList();

        return meaningful.Count > 0 ? meaningful : tokens;
    }

    private static List<TypedWord> WordsIn(Rune[] runes, int start, int end)
    {
        var words = new List<TypedWord>();
        var index = start;

        while (index < end)
        {
            if (IsWordRune(runes[index]))
            {
                var (word, next) = WordAt(runes, index, end);
                words.Add(word);
                index = next;
            }
            else
            {
                index++;
            }
        }

        return words;
    }

    // Letters and digits, with a hyphen kept between two of them: `aku-vrtačka` is one word, as the index has it.
    private static (TypedWord Word, int Next) WordAt(Rune[] runes, int start, int? end = null)
    {
        var limit = end ?? runes.Length;
        var index = start;

        while (index < limit && (IsWordRune(runes[index])
            || (runes[index].Value == '-' && index + 1 < limit && IsWordRune(runes[index + 1]) && index > start)))
        {
            index++;
        }

        var text = string.Concat(runes[start..index].Select(rune => rune.ToString()));

        return (new TypedWord(text), index);
    }

    private static bool IsWordRune(Rune rune) => Rune.IsLetterOrDigit(rune);

    private sealed record TypedWord(string Typed)
    {
        public string Lowered => Typed.ToLowerInvariant();

        // A digit makes it a code (`PH2`, `4x40`, `TS-SK-PH2100`), and so do capitals around a hyphen (`WH-BOARD-OAK`):
        // a code is matched as typed, never by its forms.
        public bool IsCode => Typed.Any(char.IsDigit)
            || (Typed.Contains('-') && Typed.Where(char.IsLetter).All(letter => char.IsUpper(letter) && char.IsAscii(letter)));
    }
}

internal enum SearchTokenKind
{
    Word,
    Code,
    Phrase,
    Excluded,
}

// One thing to match. Every kind holds one word but a phrase, which holds its words in order.
internal sealed record SearchToken(SearchTokenKind Kind, IReadOnlyList<string> Words)
{
    public string Text => Words[0];
}
