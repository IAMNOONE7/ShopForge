using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneInvoicePerOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_invoices_store_id_order_number_kind",
                schema: "orders",
                table: "invoices",
                columns: new[] { "store_id", "order_number", "kind" },
                unique: true,
                filter: "return_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_store_id_order_number_kind",
                schema: "orders",
                table: "invoices");
        }
    }
}
