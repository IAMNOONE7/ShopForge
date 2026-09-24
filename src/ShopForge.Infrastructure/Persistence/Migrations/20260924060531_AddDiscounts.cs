using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discount_code",
                schema: "orders",
                table: "orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discount_name",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_total",
                schema: "orders",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "shipping_discount",
                schema: "orders",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount",
                schema: "orders",
                table: "order_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "discount_code",
                schema: "orders",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount",
                schema: "orders",
                table: "invoice_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "discount_code",
                schema: "orders",
                table: "carts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "discounts",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    minimum_order_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_redemptions = table.Column<int>(type: "integer", nullable: true),
                    max_redemptions_per_customer = table.Column<int>(type: "integer", nullable: true),
                    redemptions = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discounts", x => x.id);
                    table.CheckConstraint("ck_discounts_value", "value >= 0 AND redemptions >= 0");
                });

            migrationBuilder.CreateTable(
                name: "discount_redemptions",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_redemptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_discount_redemptions_discounts_discount_id",
                        column: x => x.discount_id,
                        principalSchema: "orders",
                        principalTable: "discounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discount_redemptions_discount_id",
                schema: "orders",
                table: "discount_redemptions",
                column: "discount_id");

            migrationBuilder.CreateIndex(
                name: "ix_discount_redemptions_store_id_discount_id_email",
                schema: "orders",
                table: "discount_redemptions",
                columns: new[] { "store_id", "discount_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_discounts_store_id_code",
                schema: "orders",
                table: "discounts",
                columns: new[] { "store_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discount_redemptions",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "discounts",
                schema: "orders");

            migrationBuilder.DropColumn(
                name: "discount_code",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "discount_name",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "discount_total",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_discount",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "discount",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "discount_code",
                schema: "orders",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "discount",
                schema: "orders",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "discount_code",
                schema: "orders",
                table: "carts");
        }
    }
}
