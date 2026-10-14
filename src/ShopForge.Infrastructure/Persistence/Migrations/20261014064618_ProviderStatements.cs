using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_statement_days",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    lines = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_statement_days", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provider_statement_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    transfer_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    transferred_on = table.Column<DateOnly>(type: "date", nullable: true),
                    transaction_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    order_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    confirmed = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    transferred = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: true),
                    problem = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_statement_lines", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_statement_days_store_id_provider_day",
                schema: "orders",
                table: "provider_statement_days",
                columns: new[] { "store_id", "provider", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_statement_lines_store_id_provider_paid_on",
                schema: "orders",
                table: "provider_statement_lines",
                columns: new[] { "store_id", "provider", "paid_on" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_statement_lines_store_id_provider_transaction_id",
                schema: "orders",
                table: "provider_statement_lines",
                columns: new[] { "store_id", "provider", "transaction_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_statement_days",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "provider_statement_lines",
                schema: "orders");
        }
    }
}
