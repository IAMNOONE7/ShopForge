using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "stores");

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    culture = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    theme = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                    table.CheckConstraint("ck_stores_currency", "currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "fk_stores_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "stores",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_domains",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_domains", x => x.id);
                    table.CheckConstraint("ck_store_domains_host_name_normalized", "host_name = lower(host_name) AND host_name !~ '[:/\\s]'");
                    table.ForeignKey(
                        name: "fk_store_domains_store_store_id",
                        column: x => x.store_id,
                        principalSchema: "stores",
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_store_domains_host_name",
                schema: "stores",
                table: "store_domains",
                column: "host_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_domains_store_id",
                schema: "stores",
                table: "store_domains",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_domains_store_id_primary",
                schema: "stores",
                table: "store_domains",
                column: "store_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_stores_tenant_id",
                schema: "stores",
                table: "stores",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_domains",
                schema: "stores");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "stores");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "stores");
        }
    }
}
