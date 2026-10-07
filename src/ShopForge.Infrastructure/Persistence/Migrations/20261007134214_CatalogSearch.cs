using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_searchable",
                schema: "catalog",
                table: "attribute_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "search_queries",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    found = table.Column<int>(type: "integer", nullable: false),
                    asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_queries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_search_queries_store_id_asked_at",
                schema: "catalog",
                table: "search_queries",
                columns: new[] { "store_id", "asked_at" });

            // What a shopper's typing is matched against. It is not in the EF model: the type belongs to
            // PostgreSQL and the module that reads it does not reference the provider, so the column and its
            // index are stated here and the vector is written by one SQL statement in `SearchIndex`.
            migrationBuilder.Sql("ALTER TABLE catalog.store_products ADD COLUMN search_vector tsvector");
            migrationBuilder.Sql(
                "CREATE INDEX ix_store_products_search_vector ON catalog.store_products USING gin (search_vector)");

            // A catalogue that already exists is searchable the moment this runs rather than after the first
            // nightly pass. It is seeded without stemming, which is right for some shops' languages and merely
            // adequate for the rest; the nightly rebuild rewrites each store's with its own configuration, and
            // every edit in between rewrites that listing's.
            migrationBuilder.Sql(
                """
                UPDATE catalog.store_products AS listing
                SET search_vector =
                    setweight(to_tsvector('simple', coalesce(listing.name, '')), 'A')
                    || setweight(to_tsvector('simple', coalesce((
                        SELECT string_agg(variant.sku, ' ')
                        FROM catalog.product_variants AS variant
                        WHERE variant.product_id = listing.product_id), '')), 'A')
                    || setweight(to_tsvector('simple', coalesce(listing.description, '')), 'C')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS catalog.ix_store_products_search_vector");
            migrationBuilder.Sql("ALTER TABLE catalog.store_products DROP COLUMN IF EXISTS search_vector");

            migrationBuilder.DropTable(
                name: "search_queries",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "is_searchable",
                schema: "catalog",
                table: "attribute_definitions");
        }
    }
}
