using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductVatRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "vat_rate",
                schema: "catalog",
                table: "store_products",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 21m);

            // Listings created before VAT existed keep the standard rate until an admin changes them.
            migrationBuilder.Sql("ALTER TABLE catalog.store_products ALTER COLUMN vat_rate DROP DEFAULT;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_store_products_vat_rate",
                schema: "catalog",
                table: "store_products",
                sql: "vat_rate >= 0 AND vat_rate <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_store_products_vat_rate",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "vat_rate",
                schema: "catalog",
                table: "store_products");
        }
    }
}
