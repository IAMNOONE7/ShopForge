using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "carts",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_numbers",
                schema: "orders",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_numbers", x => new { x.store_id, x.year });
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    access_token = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    payment_method_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payment_method_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipping_method_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shipping_method_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipping_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    shipping_vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    billing_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    billing_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    billing_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    billing_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    billing_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    billing_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shipping_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipping_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    shipping_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shipping_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shipping_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shipping_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_shipping_price", "shipping_price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipping_methods",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipping_methods", x => x.id);
                    table.CheckConstraint("ck_shipping_methods_price", "price >= 0 AND vat_rate >= 0 AND vat_rate <= 100");
                });

            migrationBuilder.CreateTable(
                name: "cart_lines",
                schema: "orders",
                columns: table => new
                {
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_lines", x => new { x.cart_id, x.store_product_id });
                    table.CheckConstraint("ck_cart_lines_quantity", "quantity > 0 AND quantity <= 99");
                    table.ForeignKey(
                        name: "fk_cart_lines_carts_cart_id",
                        column: x => x.cart_id,
                        principalSchema: "orders",
                        principalTable: "carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "orders",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => new { x.order_id, x.id });
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_store_id_number",
                schema: "orders",
                table: "orders",
                columns: new[] { "store_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_store_id_placed_at",
                schema: "orders",
                table: "orders",
                columns: new[] { "store_id", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_store_id_code",
                schema: "orders",
                table: "payment_methods",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipping_methods_store_id_code",
                schema: "orders",
                table: "shipping_methods",
                columns: new[] { "store_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_numbers",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "shipping_methods",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "carts",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");
        }
    }
}
