namespace ShopForge.Catalog.Search;

// The questions a search asks of a listing, each as a `to_tsquery` template and the words that fill it (D-225):
// whether it is found at all, and whether every word is in its name exactly, by its forms, or only as a beginning,
// which is what ranks it. A template holds operators and `%L` alone, and PostgreSQL's `format` quotes each word into
// its place as a literal, so nothing typed is ever read as the query's own syntax.
internal sealed record SearchMatch(SearchQueryText Found, SearchQueryText ExactInName, SearchQueryText FormsInName, SearchQueryText BeginningsInName)
{
    public bool FindsNothing => Found.Words.Count == 0;

    public static SearchMatch For(IReadOnlyList<SearchToken> tokens, bool czechForms)
    {
        var found = new Template();
        var exact = new Template();
        var forms = new Template();
        var beginnings = new Template();

        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case SearchTokenKind.Word:
                    var prefixes = czechForms ? CzechWordForms.PrefixesOf(token.Text) : [token.Text];
                    found.AnyOf(prefixes, ":*");
                    exact.One(token.Text, ":A");
                    beginnings.AnyOf(prefixes, ":*A");

                    // A language PostgreSQL stems has its forms already: the word reads as its stem.
                    if (czechForms)
                    {
                        forms.AnyOf([.. prefixes.SelectMany(prefix => CzechWordForms.Endings.Select(ending => prefix + ending))], ":A");
                    }
                    else
                    {
                        forms.One(token.Text, ":A");
                    }

                    break;
                case SearchTokenKind.Code:
                    found.One(token.Text, "");

                    foreach (var tier in new[] { exact, forms, beginnings })
                    {
                        tier.One(token.Text, ":A");
                    }

                    break;
                case SearchTokenKind.Phrase:
                    found.Phrase(token.Words, "");

                    foreach (var tier in new[] { exact, forms, beginnings })
                    {
                        tier.Phrase(token.Words, ":A");
                    }

                    break;
                case SearchTokenKind.Excluded:
                    found.Not(token.Text);
                    break;
            }
        }

        return new SearchMatch(found.Text, exact.Text, forms.Text, beginnings.Text);
    }

    private sealed class Template
    {
        private readonly List<string> _parts = [];
        private readonly List<string> _words = [];

        public SearchQueryText Text => new(string.Join(" & ", _parts), _words);

        public void One(string word, string suffix)
        {
            _parts.Add("%L" + suffix);
            _words.Add(word);
        }

        public void AnyOf(IReadOnlyList<string> words, string suffix)
        {
            _parts.Add("(" + string.Join(" | ", words.Select(_ => "%L" + suffix)) + ")");
            _words.AddRange(words);
        }

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

// A `to_tsquery` template and the words for its placeholders, in order.
internal sealed record SearchQueryText(string Template, IReadOnlyList<string> Words);
