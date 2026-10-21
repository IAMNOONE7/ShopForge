using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EdgeDomainBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_operations_one_unfinished_per_store",
                schema: "provisioning",
                table: "operations");

            migrationBuilder.CreateTable(
                name: "domain_bindings",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    resource_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    validation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    validation_token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    validation_token_expires = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_domain_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "store_zones",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    name_servers = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_zones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_operations_one_unfinished_per_store",
                schema: "provisioning",
                table: "operations",
                column: "store_id",
                unique: true,
                filter: "finished_at IS NULL AND subject IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_domain_bindings_host_name",
                schema: "stores",
                table: "domain_bindings",
                column: "host_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_domain_bindings_store_id",
                schema: "stores",
                table: "domain_bindings",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_zones_name",
                schema: "stores",
                table: "store_zones",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_zones_store_id",
                schema: "stores",
                table: "store_zones",
                column: "store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "domain_bindings",
                schema: "stores");

            migrationBuilder.DropTable(
                name: "store_zones",
                schema: "stores");

            migrationBuilder.DropIndex(
                name: "ux_operations_one_unfinished_per_store",
                schema: "provisioning",
                table: "operations");

            migrationBuilder.CreateIndex(
                name: "ux_operations_one_unfinished_per_store",
                schema: "provisioning",
                table: "operations",
                column: "store_id",
                unique: true,
                filter: "finished_at IS NULL");
        }
    }
}
