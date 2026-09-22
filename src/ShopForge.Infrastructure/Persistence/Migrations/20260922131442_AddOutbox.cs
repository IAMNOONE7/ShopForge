using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "customers",
                table: "pending_registrations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "orders",
                table: "carts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // Rows that existed before these timestamps start their clock now; new rows always bring their own.
            migrationBuilder.Sql("ALTER TABLE orders.carts ALTER COLUMN updated_at DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE customers.pending_registrations ALTER COLUMN created_at DROP DEFAULT;");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_due_at",
                schema: "messaging",
                table: "outbox_messages",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_store_id_status_created_at",
                schema: "messaging",
                table: "outbox_messages",
                columns: new[] { "store_id", "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "messaging");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "customers",
                table: "pending_registrations");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "orders",
                table: "carts");
        }
    }
}
