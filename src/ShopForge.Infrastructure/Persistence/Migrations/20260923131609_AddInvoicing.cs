using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "company_city",
                schema: "stores",
                table: "stores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_country",
                schema: "stores",
                table: "stores",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_legal_name",
                schema: "stores",
                table: "stores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_line1",
                schema: "stores",
                table: "stores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_postal_code",
                schema: "stores",
                table: "stores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_registration_number",
                schema: "stores",
                table: "stores",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_vat_number",
                schema: "stores",
                table: "stores",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_provider_key",
                schema: "orders",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.Sql("ALTER TABLE orders.orders ALTER COLUMN payment_provider_key DROP DEFAULT;");

            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    buyer_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    payment_method_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    buyer_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    buyer_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    buyer_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    buyer_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    buyer_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    buyer_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seller_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    seller_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    seller_legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seller_registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    seller_vat_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            // The counters must survive: a store that restarted at 1 would collide with the order numbers it has
            // already issued, so the table is reshaped rather than replaced.
            migrationBuilder.RenameTable(name: "order_numbers", schema: "orders", newName: "number_sequences");
            migrationBuilder.Sql("ALTER TABLE orders.number_sequences RENAME CONSTRAINT pk_order_numbers TO pk_number_sequences;");
            migrationBuilder.AddColumn<string>(
                name: "series",
                schema: "orders",
                table: "number_sequences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "order");
            migrationBuilder.Sql("ALTER TABLE orders.number_sequences ALTER COLUMN series DROP DEFAULT;");
            migrationBuilder.DropPrimaryKey(name: "pk_number_sequences", schema: "orders", table: "number_sequences");
            migrationBuilder.AddPrimaryKey(
                name: "pk_number_sequences",
                schema: "orders",
                table: "number_sequences",
                columns: ["store_id", "series", "year"]);

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "orders",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => new { x.invoice_id, x.id });
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "orders",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_store_id_number",
                schema: "orders",
                table: "invoices",
                columns: new[] { "store_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_store_id_order_number",
                schema: "orders",
                table: "invoices",
                columns: new[] { "store_id", "order_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "orders");

            migrationBuilder.DropPrimaryKey(name: "pk_number_sequences", schema: "orders", table: "number_sequences");
            migrationBuilder.Sql("DELETE FROM orders.number_sequences WHERE series <> 'order';");
            migrationBuilder.DropColumn(name: "series", schema: "orders", table: "number_sequences");
            migrationBuilder.RenameTable(name: "number_sequences", schema: "orders", newName: "order_numbers");
            migrationBuilder.AddPrimaryKey(
                name: "pk_order_numbers",
                schema: "orders",
                table: "order_numbers",
                columns: ["store_id", "year"]);

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "orders");

            migrationBuilder.DropColumn(
                name: "company_city",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "company_country",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "company_legal_name",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "company_line1",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "company_postal_code",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "company_registration_number",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "company_vat_number",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "payment_provider_key",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_reference",
                schema: "orders",
                table: "orders");

        }
    }
}
