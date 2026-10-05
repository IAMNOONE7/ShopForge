using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "seo_description",
                schema: "stores",
                table: "stores",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "seo_no_index",
                schema: "stores",
                table: "stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "seo_social_image_url",
                schema: "stores",
                table: "stores",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_title_suffix",
                schema: "stores",
                table: "stores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_description",
                schema: "catalog",
                table: "store_products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "seo_no_index",
                schema: "catalog",
                table: "store_products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "seo_social_image_url",
                schema: "catalog",
                table: "store_products",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_title",
                schema: "catalog",
                table: "store_products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "page_text",
                schema: "catalog",
                table: "categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_description",
                schema: "catalog",
                table: "categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seo_title",
                schema: "catalog",
                table: "categories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seo_description",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "seo_no_index",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "seo_social_image_url",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "seo_title_suffix",
                schema: "stores",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "seo_description",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "seo_no_index",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "seo_social_image_url",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "seo_title",
                schema: "catalog",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "page_text",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "seo_description",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "seo_title",
                schema: "catalog",
                table: "categories");
        }
    }
}
