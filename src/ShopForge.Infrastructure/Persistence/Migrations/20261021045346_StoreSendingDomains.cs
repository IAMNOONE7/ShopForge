using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreSendingDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sending_domains",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    aligned_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    sender_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    reply_to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    region = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    records = table.Column<string>(type: "text", nullable: true),
                    key_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    key_secret_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sending_domains", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sending_domains_domain",
                schema: "messaging",
                table: "sending_domains",
                column: "domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sending_domains_store_id",
                schema: "messaging",
                table: "sending_domains",
                column: "store_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sending_domains",
                schema: "messaging");
        }
    }
}
