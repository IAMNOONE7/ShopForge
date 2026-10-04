using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPhoneAndShippingProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "orders",
                table: "orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_provider_key",
                schema: "orders",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Orders already placed get the carrier of the method they name, while that method is still there to
            // say so. An order whose method has since been deleted keeps a null key and could not have shipped
            // through the old lookup either, which asked the same question of the same row.
            migrationBuilder.Sql(
                """
                UPDATE orders.orders AS o
                SET shipping_provider_key = m.provider_key
                FROM orders.shipping_methods AS m
                WHERE m.store_id = o.store_id AND m.code = o.shipping_method_code
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "phone",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_provider_key",
                schema: "orders",
                table: "orders");
        }
    }
}
