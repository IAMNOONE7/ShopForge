using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnappliedPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "unapplied_payments",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unapplied_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_unapplied_payments_store_id_order_number",
                schema: "orders",
                table: "unapplied_payments",
                columns: new[] { "store_id", "order_number" });

            migrationBuilder.CreateIndex(
                name: "ix_unapplied_payments_store_id_provider_reference",
                schema: "orders",
                table: "unapplied_payments",
                columns: new[] { "store_id", "provider", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unapplied_payments_store_id_status",
                schema: "orders",
                table: "unapplied_payments",
                columns: new[] { "store_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unapplied_payments",
                schema: "orders");
        }
    }
}
