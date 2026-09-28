using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProvedDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "verification_token",
                schema: "stores",
                table: "store_domains",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                schema: "stores",
                table: "store_domains",
                type: "timestamp with time zone",
                nullable: true);

            // Every domain that already exists is a platform subdomain handed out when its store was created, and
            // the platform owns those. Leaving them unproved would take every live store off the internet.
            migrationBuilder.Sql("UPDATE stores.store_domains SET verified_at = now() WHERE verified_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "verification_token",
                schema: "stores",
                table: "store_domains");

            migrationBuilder.DropColumn(
                name: "verified_at",
                schema: "stores",
                table: "store_domains");
        }
    }
}
