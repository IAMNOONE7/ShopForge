using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreeShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "free_shipping_is_on",
                schema: "stores",
                table: "stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "free_shipping_threshold",
                schema: "stores",
                table: "stores",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "free_shipping_threshold",
                schema: "orders",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_free_shipping",
                schema: "stores",
                table: "stores",
                sql: "(free_shipping_threshold IS NULL OR free_shipping_threshold > 0) AND (NOT free_shipping_is_on OR free_shipping_threshold IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_free_shipping",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "free_shipping_is_on",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "free_shipping_threshold",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "free_shipping_threshold",
                schema: "orders",
                table: "orders");
        }
    }
}
