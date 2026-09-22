using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customers");

            migrationBuilder.AddColumn<Guid>(
                name: "store_customer_id",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_identities",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_email_verified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_identities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_tokens",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_tokens_customer_identities_customer_identity_id",
                        column: x => x.customer_identity_id,
                        principalSchema: "customers",
                        principalTable: "customer_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_registrations",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_registrations_customer_identities_customer_identity",
                        column: x => x.customer_identity_id,
                        principalSchema: "customers",
                        principalTable: "customer_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_customers",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_customers_customer_identities_customer_identity_id",
                        column: x => x.customer_identity_id,
                        principalSchema: "customers",
                        principalTable: "customer_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_store_id_email",
                schema: "orders",
                table: "orders",
                columns: new[] { "store_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_store_id_store_customer_id",
                schema: "orders",
                table: "orders",
                columns: new[] { "store_id", "store_customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_identities_tenant_id_email",
                schema: "customers",
                table: "customer_identities",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_tokens_customer_identity_id",
                schema: "customers",
                table: "customer_tokens",
                column: "customer_identity_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_tokens_token_hash",
                schema: "customers",
                table: "customer_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_registrations_customer_identity_id",
                schema: "customers",
                table: "pending_registrations",
                column: "customer_identity_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_registrations_store_id_customer_identity_id",
                schema: "customers",
                table: "pending_registrations",
                columns: new[] { "store_id", "customer_identity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_customers_customer_identity_id",
                schema: "customers",
                table: "store_customers",
                column: "customer_identity_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_customers_store_id_customer_identity_id",
                schema: "customers",
                table: "store_customers",
                columns: new[] { "store_id", "customer_identity_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_tokens",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "pending_registrations",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "store_customers",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customer_identities",
                schema: "customers");

            migrationBuilder.DropIndex(
                name: "ix_orders_store_id_email",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_store_id_store_customer_id",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "store_customer_id",
                schema: "orders",
                table: "orders");
        }
    }
}
