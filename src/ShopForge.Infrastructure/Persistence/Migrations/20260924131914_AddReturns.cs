using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "return_window_days",
                schema: "stores",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<decimal>(
                name: "refunded_total",
                schema: "orders",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "return_id",
                schema: "orders",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_returns",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    store_customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_returns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_return_lines",
                schema: "orders",
                columns: table => new
                {
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_return_lines", x => new { x.order_return_id, x.store_product_id });
                    table.CheckConstraint("ck_order_return_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_order_return_lines_order_returns_order_return_id",
                        column: x => x.order_return_id,
                        principalSchema: "orders",
                        principalTable: "order_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stores_return_window_days",
                schema: "stores",
                table: "stores",
                sql: "return_window_days >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_return_id",
                schema: "orders",
                table: "invoices",
                column: "return_id",
                unique: true,
                filter: "return_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_order_returns_store_id_number",
                schema: "orders",
                table: "order_returns",
                columns: new[] { "store_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_returns_store_id_order_number",
                schema: "orders",
                table: "order_returns",
                columns: new[] { "store_id", "order_number" });

            migrationBuilder.CreateIndex(
                name: "ix_order_returns_store_id_status",
                schema: "orders",
                table: "order_returns",
                columns: new[] { "store_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_return_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_returns",
                schema: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stores_return_window_days",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropIndex(
                name: "ix_invoices_return_id",
                schema: "orders",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_window_days",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "refunded_total",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "return_id",
                schema: "orders",
                table: "invoices");
        }
    }
}
