using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VariantSizes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "height_mm",
                schema: "catalog",
                table: "product_variants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "length_mm",
                schema: "catalog",
                table: "product_variants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ships_alone",
                schema: "catalog",
                table: "product_variants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "width_mm",
                schema: "catalog",
                table: "product_variants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_product_variants_sizes",
                schema: "catalog",
                table: "product_variants",
                sql: "(length_mm IS NULL AND width_mm IS NULL AND height_mm IS NULL) OR (length_mm > 0 AND width_mm > 0 AND height_mm > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_product_variants_sizes",
                schema: "catalog",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "height_mm",
                schema: "catalog",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "length_mm",
                schema: "catalog",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "ships_alone",
                schema: "catalog",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "width_mm",
                schema: "catalog",
                table: "product_variants");
        }
    }
}
