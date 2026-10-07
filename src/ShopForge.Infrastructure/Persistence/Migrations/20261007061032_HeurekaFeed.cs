using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HeurekaFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_in_feeds",
                schema: "catalog",
                table: "attribute_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "category_feed_mappings",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feed = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    engine_category = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_feed_mappings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_category_feed_mappings_store_id_category_id_feed",
                schema: "catalog",
                table: "category_feed_mappings",
                columns: new[] { "store_id", "category_id", "feed" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_feed_mappings",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "is_in_feeds",
                schema: "catalog",
                table: "attribute_definitions");
        }
    }
}
