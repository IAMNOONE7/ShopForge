using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RateCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rates_from",
                schema: "orders",
                table: "shipping_methods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "rates_priced_on",
                schema: "orders",
                table: "shipping_methods",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shipping_rate_bands",
                schema: "orders",
                columns: table => new
                {
                    shipping_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    up_to_grams = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipping_rate_bands", x => new { x.shipping_method_id, x.id });
                    table.CheckConstraint("ck_shipping_rate_bands_values", "up_to_grams > 0 AND price >= 0");
                    table.ForeignKey(
                        name: "fk_shipping_rate_bands_shipping_methods_shipping_method_id",
                        column: x => x.shipping_method_id,
                        principalSchema: "orders",
                        principalTable: "shipping_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipping_rate_bands",
                schema: "orders");

            migrationBuilder.DropColumn(
                name: "rates_from",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "rates_priced_on",
                schema: "orders",
                table: "shipping_methods");
        }
    }
}
