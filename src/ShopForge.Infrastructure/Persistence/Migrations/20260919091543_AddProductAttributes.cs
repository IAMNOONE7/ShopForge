using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attribute_definitions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_filterable = table.Column<bool>(type: "boolean", nullable: false),
                    is_visible_on_product_page = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_definitions", x => x.id);
                    table.UniqueConstraint("ak_attribute_definitions_store_id_id", x => new { x.store_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "attribute_options",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_options", x => x.id);
                    table.UniqueConstraint("ak_attribute_options_store_id_attribute_definition_id_id", x => new { x.store_id, x.attribute_definition_id, x.id });
                    table.ForeignKey(
                        name: "fk_attribute_options_attribute_definitions_store_id_attribute_",
                        columns: x => new { x.store_id, x.attribute_definition_id },
                        principalSchema: "catalog",
                        principalTable: "attribute_definitions",
                        principalColumns: new[] { "store_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_attributes",
                schema: "catalog",
                columns: table => new
                {
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_attributes", x => new { x.category_id, x.attribute_definition_id });
                    table.ForeignKey(
                        name: "fk_category_attributes_attribute_definitions_store_id_attribut",
                        columns: x => new { x.store_id, x.attribute_definition_id },
                        principalSchema: "catalog",
                        principalTable: "attribute_definitions",
                        principalColumns: new[] { "store_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_category_attributes_categories_store_id_category_id",
                        columns: x => new { x.store_id, x.category_id },
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumns: new[] { "store_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_attribute_values",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    integer_value = table.Column<long>(type: "bigint", nullable: true),
                    decimal_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    boolean_value = table.Column<bool>(type: "boolean", nullable: true),
                    date_value = table.Column<DateOnly>(type: "date", nullable: true),
                    option_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_attribute_values", x => x.id);
                    table.CheckConstraint("ck_product_attribute_values_single_value", "num_nonnulls(text_value, integer_value, decimal_value, boolean_value, date_value, option_id) = 1");
                    table.ForeignKey(
                        name: "fk_product_attribute_values_attribute_definitions_store_id_att",
                        columns: x => new { x.store_id, x.attribute_definition_id },
                        principalSchema: "catalog",
                        principalTable: "attribute_definitions",
                        principalColumns: new[] { "store_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_attribute_values_attribute_options_store_id_attribu",
                        columns: x => new { x.store_id, x.attribute_definition_id, x.option_id },
                        principalSchema: "catalog",
                        principalTable: "attribute_options",
                        principalColumns: new[] { "store_id", "attribute_definition_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_attribute_values_store_products_store_id_store_prod",
                        columns: x => new { x.store_id, x.store_product_id },
                        principalSchema: "catalog",
                        principalTable: "store_products",
                        principalColumns: new[] { "store_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attribute_definitions_store_id_code",
                schema: "catalog",
                table: "attribute_definitions",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_options_attribute_definition_id_code",
                schema: "catalog",
                table: "attribute_options",
                columns: new[] { "attribute_definition_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_attributes_store_id_attribute_definition_id",
                schema: "catalog",
                table: "category_attributes",
                columns: new[] { "store_id", "attribute_definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_category_attributes_store_id_category_id",
                schema: "catalog",
                table: "category_attributes",
                columns: new[] { "store_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_attribute_definition_id_date_value",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "attribute_definition_id", "date_value" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_attribute_definition_id_decimal_va",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "attribute_definition_id", "decimal_value" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_attribute_definition_id_integer_va",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "attribute_definition_id", "integer_value" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_option",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "store_product_id", "attribute_definition_id", "option_id" },
                unique: true,
                filter: "option_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_single",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "store_product_id", "attribute_definition_id" },
                unique: true,
                filter: "option_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_store_id_attribute_definition_id_o",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "store_id", "attribute_definition_id", "option_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_attribute_values_store_id_store_product_id",
                schema: "catalog",
                table: "product_attribute_values",
                columns: new[] { "store_id", "store_product_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_attributes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_attribute_values",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "attribute_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "attribute_definitions",
                schema: "catalog");
        }
    }
}
