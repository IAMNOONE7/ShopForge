using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderParcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_parcels",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    gross_weight_grams = table.Column<int>(type: "integer", nullable: false),
                    band_up_to_grams = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    alone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_parcels", x => x.id);
                    table.CheckConstraint("ck_order_parcels_values", "number >= 1 AND gross_weight_grams > 0 AND band_up_to_grams >= gross_weight_grams AND price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "order_parcel_items",
                schema: "orders",
                columns: table => new
                {
                    order_parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_parcel_items", x => new { x.order_parcel_id, x.id });
                    table.CheckConstraint("ck_order_parcel_items_quantity", "quantity >= 1");
                    table.ForeignKey(
                        name: "fk_order_parcel_items_order_parcels_order_parcel_id",
                        column: x => x.order_parcel_id,
                        principalSchema: "orders",
                        principalTable: "order_parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_parcels_store_id_order_number_number",
                schema: "orders",
                table: "order_parcels",
                columns: new[] { "store_id", "order_number", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_parcel_items",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_parcels",
                schema: "orders");
        }
    }
}
