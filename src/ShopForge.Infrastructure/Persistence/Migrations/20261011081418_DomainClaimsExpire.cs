using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DomainClaimsExpire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                schema: "stores",
                table: "store_domains",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // EF's default for a new required column is the sentinel date, which would make every claim already
            // on the platform look lapsed — and the sweep deletes a lapsed claim. A proved name takes the date
            // it was proved, which is history rather than a countdown; an unproved one starts its window now,
            // so nobody's pending claim is destroyed by the upgrade. The column default goes afterwards,
            // because every insert supplies the value and a lingering default is a trap for the next migration.
            migrationBuilder.Sql(
                """
                UPDATE stores.store_domains SET claimed_at = verified_at WHERE verified_at IS NOT NULL;
                UPDATE stores.store_domains SET claimed_at = now() WHERE verified_at IS NULL;
                ALTER TABLE stores.store_domains ALTER COLUMN claimed_at DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_store_domains_verified_at_claimed_at",
                schema: "stores",
                table: "store_domains",
                columns: new[] { "verified_at", "claimed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_store_domains_verified_at_claimed_at",
                schema: "stores",
                table: "store_domains");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                schema: "stores",
                table: "store_domains");
        }
    }
}
