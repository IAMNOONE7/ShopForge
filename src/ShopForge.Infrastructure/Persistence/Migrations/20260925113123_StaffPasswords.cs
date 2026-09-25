using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "security_stamp",
                schema: "access",
                table: "tenant_users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Everybody who already works here gets a stamp of their own, so one person's sessions ending never
            // ends anybody else's. Their current cookies carry no stamp at all and are rejected once, which is the
            // one sign-out this change costs.
            migrationBuilder.Sql("UPDATE access.tenant_users SET security_stamp = gen_random_uuid();");

            migrationBuilder.CreateTable(
                name: "password_resets",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_resets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_password_resets_tenant_user_id",
                schema: "access",
                table: "password_resets",
                column: "tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_resets_token_hash",
                schema: "access",
                table: "password_resets",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_resets",
                schema: "access");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                schema: "access",
                table: "tenant_users");
        }
    }
}
