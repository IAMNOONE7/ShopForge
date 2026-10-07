using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "store_feeds",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feed = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    file_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_milliseconds = table.Column<int>(type: "integer", nullable: true),
                    last_bytes = table.Column<long>(type: "bigint", nullable: true),
                    last_products = table.Column<int>(type: "integer", nullable: true),
                    last_skipped = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_feeds", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_store_feeds_store_id_feed",
                schema: "catalog",
                table: "store_feeds",
                columns: new[] { "store_id", "feed" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_feeds_token",
                schema: "catalog",
                table: "store_feeds",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_feeds",
                schema: "catalog");
        }
    }
}
