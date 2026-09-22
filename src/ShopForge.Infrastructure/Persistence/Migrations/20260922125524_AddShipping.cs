using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_pickup_point",
                schema: "orders",
                table: "shipping_methods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_city",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_code",
                schema: "orders",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_country",
                schema: "orders",
                table: "orders",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_full_name",
                schema: "orders",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_line1",
                schema: "orders",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_line2",
                schema: "orders",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_name",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_point_postal_code",
                schema: "orders",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipment_carrier",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shipment_shipped_at",
                schema: "orders",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipment_tracking_number",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipment_tracking_url",
                schema: "orders",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pickup_points",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    address_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_points", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pickup_points_store_id_code",
                schema: "orders",
                table: "pickup_points",
                columns: new[] { "store_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pickup_points",
                schema: "orders");

            migrationBuilder.DropColumn(
                name: "requires_pickup_point",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "pickup_point_city",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_point_code",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_point_country",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_point_full_name",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_point_line1",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_point_line2",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_point_name",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_point_postal_code",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipment_carrier",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipment_shipped_at",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipment_tracking_number",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipment_tracking_url",
                schema: "orders",
                table: "orders");
        }
    }
}
