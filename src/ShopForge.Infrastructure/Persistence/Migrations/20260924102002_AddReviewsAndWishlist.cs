using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewsAndWishlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "rating_average",
                schema: "catalog",
                table: "store_products",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                schema: "catalog",
                table: "store_products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "product_reviews",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_reviews", x => x.id);
                    table.CheckConstraint("ck_product_reviews_rating", "rating >= 1 AND rating <= 5");
                    table.ForeignKey(
                        name: "fk_product_reviews_store_products_store_id_store_product_id",
                        columns: x => new { x.store_id, x.store_product_id },
                        principalSchema: "catalog",
                        principalTable: "store_products",
                        principalColumns: new[] { "store_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_items",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_wishlist_items_store_customers_store_customer_id",
                        column: x => x.store_customer_id,
                        principalSchema: "customers",
                        principalTable: "store_customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_reviews_store_id_status_written_at",
                schema: "catalog",
                table: "product_reviews",
                columns: new[] { "store_id", "status", "written_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_reviews_store_id_store_product_id",
                schema: "catalog",
                table: "product_reviews",
                columns: new[] { "store_id", "store_product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_reviews_store_product_id_store_customer_id",
                schema: "catalog",
                table: "product_reviews",
                columns: new[] { "store_product_id", "store_customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_items_store_customer_id_store_product_id",
                schema: "customers",
                table: "wishlist_items",
                columns: new[] { "store_customer_id", "store_product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_reviews",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "wishlist_items",
                schema: "customers");

            migrationBuilder.DropColumn(
                name: "rating_average",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "rating_count",
                schema: "catalog",
                table: "store_products");
        }
    }
}
