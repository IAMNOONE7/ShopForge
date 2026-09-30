using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_tenant_id_sku",
                schema: "catalog",
                table: "products");

            migrationBuilder.AlterColumn<string>(
                name: "sku",
                schema: "catalog",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string[]>(
                name: "option_names",
                schema: "catalog",
                table: "products",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "product_variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ean = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    weight_grams = table.Column<int>(type: "integer", nullable: true),
                    option_values = table.Column<string[]>(type: "text[]", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                    table.CheckConstraint("ck_product_variants_weight_grams", "weight_grams IS NULL OR weight_grams >= 0");
                    table.ForeignKey(
                        name: "fk_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_product_id",
                schema: "catalog",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_tenant_id_sku",
                schema: "catalog",
                table: "product_variants",
                columns: new[] { "tenant_id", "sku" },
                unique: true);

            // Every product that existed is a product sold in one form, so it becomes a product with one
            // variant carrying what the product used to carry itself. Without this the catalog would have
            // nothing behind it: a product with no variants has no SKU, no weight and nothing to sell.
            migrationBuilder.Sql(
                """
                INSERT INTO catalog.product_variants (id, tenant_id, product_id, sku, ean, weight_grams, option_values, position)
                SELECT gen_random_uuid(), tenant_id, id, sku, ean, weight_grams, ARRAY[]::text[], 0
                FROM catalog.products
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_variants",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "option_names",
                schema: "catalog",
                table: "products");

            migrationBuilder.AlterColumn<string>(
                name: "sku",
                schema: "catalog",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_sku",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "sku" },
                unique: true);
        }
    }
}
