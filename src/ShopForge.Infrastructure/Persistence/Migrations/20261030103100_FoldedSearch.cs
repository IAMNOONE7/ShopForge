using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FoldedSearch : Migration
    {
        // The languages PostgreSQL stems, by the culture's language, as `SearchConfigurations` named them when this was
        // written. Any other language is read without stemming.
        private static readonly (string Language, string Configuration)[] Stemmed =
        [
            ("da", "danish"), ("de", "german"), ("en", "english"), ("es", "spanish"), ("fi", "finnish"),
            ("fr", "french"), ("hu", "hungarian"), ("it", "italian"), ("nl", "dutch"), ("no", "norwegian"),
            ("pt", "portuguese"), ("ro", "romanian"), ("ru", "russian"), ("sv", "swedish"), ("tr", "turkish"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A contrib module the server ships. Azure allows only what `azure.extensions` lists, which the data template
            // sets before any release migrates (D-224); everywhere else the database's owner may create it.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent");

            // PostgreSQL's own pattern for search without accents: a configuration copied from the language's, with
            // `unaccent` in front of its dictionary. It builds the vector and reads the query, so both sides fold the
            // same way. Only words with a letter beyond ASCII can carry an accent, and those are the token types changed
            // here; numbers and mixed words such as `2místný` keep `simple` behind it, as their language had.
            migrationBuilder.Sql("CREATE TEXT SEARCH CONFIGURATION catalog.simple_folded (COPY = simple)");
            migrationBuilder.Sql(
                "ALTER TEXT SEARCH CONFIGURATION catalog.simple_folded "
                + "ALTER MAPPING FOR word, hword, hword_part, numword, numhword, hword_numpart WITH unaccent, simple");

            foreach (var (_, configuration) in Stemmed)
            {
                migrationBuilder.Sql($"CREATE TEXT SEARCH CONFIGURATION catalog.{configuration}_folded (COPY = {configuration})");
                migrationBuilder.Sql(
                    $"ALTER TEXT SEARCH CONFIGURATION catalog.{configuration}_folded "
                    + $"ALTER MAPPING FOR word, hword, hword_part WITH unaccent, {configuration}_stem");
                migrationBuilder.Sql(
                    $"ALTER TEXT SEARCH CONFIGURATION catalog.{configuration}_folded "
                    + "ALTER MAPPING FOR numword, numhword, hword_numpart WITH unaccent, simple");
            }

            // Beside the vector the previous build reads, rather than instead of it: that build keeps serving while
            // this runs and comes back on a rollback. V29i drops the old one once a release has crossed this (V11).
            migrationBuilder.Sql("ALTER TABLE catalog.store_products ADD COLUMN search_vector_folded tsvector");
            migrationBuilder.Sql(
                "CREATE INDEX ix_store_products_search_vector_folded ON catalog.store_products USING gin (search_vector_folded)");

            // Every store's catalogue is searchable without accents the moment this runs, each in its own language:
            // the statement `SearchIndex` runs, with the configuration chosen here from the store's culture.
            var configurations = string.Join(
                " ",
                Stemmed.Select(language => $"WHEN '{language.Language}' THEN 'catalog.{language.Configuration}_folded'"));

            migrationBuilder.Sql(
                $"""
                UPDATE catalog.store_products AS listing
                SET search_vector_folded =
                    setweight(to_tsvector(store.configuration, coalesce(listing.name, '')), 'A')
                    || setweight(to_tsvector('catalog.simple_folded', coalesce((
                        SELECT string_agg(variant.sku, ' ')
                        FROM catalog.product_variants AS variant
                        WHERE variant.product_id = listing.product_id), '')), 'A')
                    || setweight(to_tsvector(store.configuration, coalesce(listing.description, '')), 'C')
                    || setweight(to_tsvector(store.configuration, coalesce((
                        SELECT string_agg(value.text_value, ' ')
                        FROM catalog.product_attribute_values AS value
                        JOIN catalog.attribute_definitions AS definition
                            ON definition.id = value.attribute_definition_id AND definition.is_searchable
                        WHERE value.store_product_id = listing.id), '')), 'D')
                FROM (
                    SELECT id, (CASE split_part(lower(culture), '-', 1) {configurations} ELSE 'catalog.simple_folded' END)::regconfig AS configuration
                    FROM stores.stores) AS store
                WHERE store.id = listing.store_id
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS catalog.ix_store_products_search_vector_folded");
            migrationBuilder.Sql("ALTER TABLE catalog.store_products DROP COLUMN IF EXISTS search_vector_folded");

            foreach (var (_, configuration) in Stemmed)
            {
                migrationBuilder.Sql($"DROP TEXT SEARCH CONFIGURATION IF EXISTS catalog.{configuration}_folded");
            }

            // The extension stays: it may have been installed before this migration, and nothing left uses it.
            migrationBuilder.Sql("DROP TEXT SEARCH CONFIGURATION IF EXISTS catalog.simple_folded");
        }
    }
}
