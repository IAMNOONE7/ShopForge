using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReturnRefundState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refund_note",
                schema: "orders",
                table: "order_returns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_problem",
                schema: "orders",
                table: "order_returns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_settled_at",
                schema: "orders",
                table: "order_returns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_status",
                schema: "orders",
                table: "order_returns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // A return received before this was refunded inside the receipt, or left to the store for a method it
            // settles itself, so that is what it is recorded as.
            migrationBuilder.Sql(
                """
                UPDATE orders.order_returns AS r
                SET refund_status = CASE WHEN o.payment_provider_key = 'manual' AND r.refunded_amount > 0 THEN 'ByStore' ELSE 'Refunded' END,
                    refund_settled_at = r.received_at
                FROM orders.orders AS o
                WHERE o.store_id = r.store_id AND o.number = r.order_number AND r.status = 'Received';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_order_returns_store_id_refund_status",
                schema: "orders",
                table: "order_returns",
                columns: new[] { "store_id", "refund_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_returns_store_id_refund_status",
                schema: "orders",
                table: "order_returns");

            migrationBuilder.DropColumn(
                name: "refund_note",
                schema: "orders",
                table: "order_returns");

            migrationBuilder.DropColumn(
                name: "refund_problem",
                schema: "orders",
                table: "order_returns");

            migrationBuilder.DropColumn(
                name: "refund_settled_at",
                schema: "orders",
                table: "order_returns");

            migrationBuilder.DropColumn(
                name: "refund_status",
                schema: "orders",
                table: "order_returns");
        }
    }
}
