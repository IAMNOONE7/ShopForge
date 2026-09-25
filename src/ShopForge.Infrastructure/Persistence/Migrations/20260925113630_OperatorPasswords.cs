using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperatorPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "security_stamp",
                schema: "platform",
                table: "platform_users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // A stamp of their own for every operator who already exists, so one signing out never signs out
            // another; their current cookies carry no stamp and are rejected once.
            migrationBuilder.Sql("UPDATE platform.platform_users SET security_stamp = gen_random_uuid();");

            migrationBuilder.CreateTable(
                name: "platform_invitations",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_invitations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_password_resets",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_password_resets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_invitations_email",
                schema: "platform",
                table: "platform_invitations",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_platform_invitations_token_hash",
                schema: "platform",
                table: "platform_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_password_resets_platform_user_id",
                schema: "platform",
                table: "platform_password_resets",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_password_resets_token_hash",
                schema: "platform",
                table: "platform_password_resets",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_invitations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "platform_password_resets",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                schema: "platform",
                table: "platform_users");
        }
    }
}
