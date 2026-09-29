using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecondFactorForOperators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "two_factor_enabled_at",
                schema: "platform",
                table: "platform_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_factor_secret",
                schema: "platform",
                table: "platform_users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_recovery_codes",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_recovery_codes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_recovery_codes_code_hash",
                schema: "platform",
                table: "platform_recovery_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_recovery_codes_platform_user_id",
                schema: "platform",
                table: "platform_recovery_codes",
                column: "platform_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_recovery_codes",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled_at",
                schema: "platform",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "two_factor_secret",
                schema: "platform",
                table: "platform_users");
        }
    }
}
