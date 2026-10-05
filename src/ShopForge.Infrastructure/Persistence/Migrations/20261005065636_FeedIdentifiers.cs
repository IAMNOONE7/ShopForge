using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeedIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "brand",
                schema: "catalog",
                table: "products",
                type: "character varying(70)",
                maxLength: 70,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "condition",
                schema: "catalog",
                table: "product_variants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "part_number",
                schema: "catalog",
                table: "product_variants",
                type: "character varying(70)",
                maxLength: 70,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brand",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "condition",
                schema: "catalog",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "part_number",
                schema: "catalog",
                table: "product_variants");
        }
    }
}
