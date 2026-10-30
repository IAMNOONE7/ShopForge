namespace ShopForge.Catalog.Search;

// The questions a search asks of a listing, each as a `to_tsquery` template and the words that fill it (D-225):
// whether it is found at all, and whether every word is in its name exactly, by its forms, or only as a beginning, or
// in what else it is called (its keywords, brand and categories, D-226), which is what ranks it. A template holds
// operators and `%L` alone, and PostgreSQL's `format` quotes each word into its place as a literal, so nothing typed
// is ever read as the query's own syntax.
internal sealed record SearchMatch(
    SearchQueryText Found,
    SearchQueryText ExactInName,
    SearchQueryText FormsInName,
    SearchQueryText BeginningsInName,
    SearchQueryText InWhatItIsCalled)
{
    public bool FindsNothing => Found.Words.Count == 0 && Found.Codes.Count == 0 && Found.Unfinished is null;

    public static SearchMatch For(IReadOnlyList<SearchToken> tokens, bool czechForms)
    {
        var found = new Template();
        var exact = new Template();
        var forms = new Template();
        var beginnings = new Template();
        var called = new Template();

        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case SearchTokenKind.Word:
                    var prefixes = czechForms ? CzechWordForms.PrefixesOf(token.Text) : [token.Text];
                    exact.One(token.Text, ":A");
                    forms.Forms(prefixes, token.Text, czechForms);

                    // A word somebody may still be typing is also asked for as typed, without stemming, wherever a
                    // beginning finds it: the store's language may drop it as a stop word, as English drops the "so" of
                    // "sofa", or stem it into something no word begins with (D-227).
                    foreach (var (question, suffix) in new[] { (found, ":*"), (beginnings, ":*A"), (called, ":*AB") })
                    {
                        if (token.IsBeginning)
                        {
                            question.Beginning(prefixes, token.Text, suffix);
                        }
                        else
                        {
                            question.AnyOf(prefixes, suffix);
                        }
                    }

                    break;
                // A code is read without stemming in every language, as the index holds it: "RENAMED-OAK-7" stays
                // "renamed" rather than becoming English's "renam" (D-226). One somebody may still be typing is the
                // beginning of a code (D-227).
                case SearchTokenKind.Code:
                    var (anywhere, inName, asCalled) = token.IsBeginning ? (":*", ":*A", ":*AB") : ("", ":A", ":AB");
                    found.Code(token.Text, anywhere);

                    foreach (var tier in new[] { exact, forms, beginnings })
                    {
                        tier.Code(token.Text, inName);
                    }

                    called.Code(token.Text, asCalled);
                    break;
                case SearchTokenKind.Phrase:
                    found.Phrase(token.Words, "");

                    foreach (var tier in new[] { exact, forms, beginnings })
                    {
                        tier.Phrase(token.Words, ":A");
                    }

                    called.Phrase(token.Words, ":AB");
                    break;
                case SearchTokenKind.Excluded:
                    found.Not(token.Text);
                    break;
            }
        }

        return new SearchMatch(found.Text, exact.Text, forms.Text, beginnings.Text, called.Text);
    }

    private sealed class Template
    {
        private readonly List<string> _parts = [];
        private readonly List<string> _words = [];
        private readonly List<string> _codeParts = [];
        private readonly List<string> _codes = [];

        private SearchBeginning? _unfinished;

        public SearchQueryText Text => new(string.Join(" & ", _parts), _words, string.Join(" & ", _codeParts), _codes, _unfinished);

        public void One(string word, string suffix)
        {
            _parts.Add("%L" + suffix);
            _words.Add(word);
        }

        public void Code(string code, string suffix)
        {
            _codeParts.Add("%L" + suffix);
            _codes.Add(code);
        }

        public void AnyOf(IReadOnlyList<string> words, string suffix)
        {
            _parts.Add("(" + string.Join(" | ", words.Select(_ => "%L" + suffix)) + ")");
            _words.AddRange(words);
        }

        // A language PostgreSQL stems has its forms already: the word reads as its stem.
        public void Forms(IReadOnlyList<string> prefixes, string word, bool czechForms)
        {
            if (czechForms)
            {
                AnyOf([.. prefixes.SelectMany(prefix => CzechWordForms.Endings.Select(ending => prefix + ending))], ":A");
            }
            else
            {
                One(word, ":A");
            }
        }

        // Only the last word can be unfinished, so a question has one at most.
        public void Beginning(IReadOnlyList<string> prefixes, string typed, string suffix) =>
            _unfinished = new SearchBeginning("(" + string.Join(" | ", prefixes.Select(_ => "%L" + suffix)) + ")", prefixes, "%L" + suffix, typed);

        public void Phrase(IReadOnlyList<string> words, string suffix)
        {
            _parts.Add("(" + string.Join(" <-> ", words.Select(_ => "%L" + suffix)) + ")");
            _words.AddRange(words);
        }

        public void Not(string word)
        {
            _parts.Add("!%L");
            _words.Add(word);
        }
    }
}

// A `to_tsquery` template and the words for its placeholders, in order, read in the store's language; the same for
// the codes, read without stemming; and a word somebody may still be typing. All must match.
internal sealed record SearchQueryText(
    string Template,
    IReadOnlyList<string> Words,
    string CodesTemplate,
    IReadOnlyList<string> Codes,
    SearchBeginning? Unfinished = null);

// An unfinished word, found either way: by its beginnings read in the store's language, or by its beginning as typed,
// read without stemming (D-227).
internal sealed record SearchBeginning(string Template, IReadOnlyList<string> Words, string AsTypedTemplate, string AsTyped);
