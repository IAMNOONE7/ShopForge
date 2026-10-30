using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchSources : Migration
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
            migrationBuilder.AddColumn<string>(
                name: "search_keywords",
                schema: "catalog",
                table: "store_products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_keywords",
                schema: "catalog",
                table: "categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                schema: "catalog",
                table: "categories",
                type: "text",
                nullable: true);

            // Each live category's words, its own name first and then those above it, five at most, joined as
            // `CategorySearchText` joins them, so the application's first rewrite finds nothing to change. Nobody has
            // written keywords yet. An archived category has none, and its name leaves those beneath it.
            migrationBuilder.Sql(
                """
                WITH RECURSIVE chain AS (
                    SELECT category.id AS category_id, category.id AS member_id, category.parent_id, 1 AS depth
                    FROM catalog.categories AS category
                    WHERE category.archived_at IS NULL
                    UNION ALL
                    SELECT chain.category_id, parent.id, parent.parent_id, chain.depth + 1
                    FROM chain
                    JOIN catalog.categories AS parent ON parent.id = chain.parent_id
                    WHERE chain.depth < 5)
                UPDATE catalog.categories AS category
                SET search_text = (
                    SELECT string_agg(member.name, ' ' ORDER BY chain.depth)
                    FROM chain
                    JOIN catalog.categories AS member ON member.id = chain.member_id
                    WHERE chain.category_id = category.id AND member.archived_at IS NULL)
                WHERE category.archived_at IS NULL
                """);

            // Every listing is found by its brand, barcodes, part numbers, categories and searchable options the moment
            // this runs, in its own store's language: the statement `SearchIndex` runs, with the configuration chosen
            // here from the store's culture. The vector the previous build reads is left as it wrote it.
            var configurations = string.Join(
                " ",
                Stemmed.Select(language => $"WHEN '{language.Language}' THEN 'catalog.{language.Configuration}_folded'"));

            migrationBuilder.Sql(
                $"""
                UPDATE catalog.store_products AS listing
                SET search_vector_folded =
                    setweight(to_tsvector(store.configuration, coalesce(listing.name, '')), 'A')
                    || setweight(to_tsvector('catalog.simple_folded', coalesce((
                        SELECT string_agg(concat_ws(' ', variant.sku, variant.ean, variant.part_number), ' ')
                        FROM catalog.product_variants AS variant
                        WHERE variant.product_id = listing.product_id), '')), 'A')
                    || setweight(to_tsvector(store.configuration, concat_ws(' ',
                        listing.search_keywords,
                        (SELECT product.brand FROM catalog.products AS product WHERE product.id = listing.product_id),
                        (SELECT string_agg(category.search_text, ' ')
                         FROM catalog.product_categories AS assignment
                         JOIN catalog.categories AS category ON category.id = assignment.category_id
                         WHERE assignment.store_product_id = listing.id))), 'B')
                    || setweight(to_tsvector(store.configuration, coalesce(listing.description, '')), 'C')
                    || setweight(to_tsvector(store.configuration, coalesce((
                        SELECT string_agg(coalesce(value.text_value, choice.name), ' ')
                        FROM catalog.product_attribute_values AS value
                        JOIN catalog.attribute_definitions AS definition
                            ON definition.id = value.attribute_definition_id AND definition.is_searchable
                        LEFT JOIN catalog.attribute_options AS choice ON choice.id = value.option_id
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
            migrationBuilder.DropColumn(
                name: "search_keywords",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "search_keywords",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "search_text",
                schema: "catalog",
                table: "categories");
        }
    }
}
