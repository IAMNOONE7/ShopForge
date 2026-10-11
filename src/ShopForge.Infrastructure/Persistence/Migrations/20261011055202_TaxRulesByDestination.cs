using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxRulesByDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tax_class",
                schema: "catalog",
                table: "store_products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_class",
                schema: "orders",
                table: "shipping_methods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tax_classes",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_classes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_rates",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rates", x => x.id);
                    table.CheckConstraint("ck_tax_rates_rate", "rate >= 0 AND rate <= 100");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_classes_store_id_code",
                schema: "stores",
                table: "tax_classes",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_rates_store_id_tax_class_country",
                schema: "stores",
                table: "tax_rates",
                columns: new[] { "store_id", "tax_class", "country" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_classes",
                schema: "stores");

            migrationBuilder.DropTable(
                name: "tax_rates",
                schema: "stores");

            migrationBuilder.DropColumn(
                name: "tax_class",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "tax_class",
                schema: "orders",
                table: "shipping_methods");
        }
    }
}
