using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProvisioningOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "provisioning");

            migrationBuilder.CreateTable(
                name: "operations",
                schema: "provisioning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    input = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    leased_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operation_steps",
                schema: "provisioning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created = table.Column<bool>(type: "boolean", nullable: false),
                    request_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data = table.Column<string>(type: "text", nullable: true),
                    secret_for = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    waiting_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_error_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_operation_steps_operations_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "provisioning",
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operation_steps_operation_id_position",
                schema: "provisioning",
                table: "operation_steps",
                columns: new[] { "operation_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operations_next_check_at",
                schema: "provisioning",
                table: "operations",
                column: "next_check_at",
                filter: "finished_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_operations_store_id_created_at",
                schema: "provisioning",
                table: "operations",
                columns: new[] { "store_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_operations_one_unfinished_per_store",
                schema: "provisioning",
                table: "operations",
                column: "store_id",
                unique: true,
                filter: "finished_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_operations_one_unfinished_per_subject",
                schema: "provisioning",
                table: "operations",
                column: "subject",
                unique: true,
                filter: "finished_at IS NULL AND subject IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_steps",
                schema: "provisioning");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "provisioning");
        }
    }
}
