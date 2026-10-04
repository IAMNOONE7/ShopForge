using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShippingMethodLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "countries",
                schema: "orders",
                table: "shipping_methods",
                type: "character(2)[]",
                nullable: false,
                // Methods already configured serve everywhere, which is what they did before there was a column
                // to say otherwise. Without this the column cannot be added to a table that has rows at all.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "max_weight_grams",
                schema: "orders",
                table: "shipping_methods",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "countries",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "max_weight_grams",
                schema: "orders",
                table: "shipping_methods");
        }
    }
}
