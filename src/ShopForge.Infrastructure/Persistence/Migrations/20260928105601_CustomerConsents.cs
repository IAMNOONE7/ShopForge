using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_consents",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_granted = table.Column<bool>(type: "boolean", nullable: false),
                    statement = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_consents_store_customer_store_customer_id",
                        column: x => x.store_customer_id,
                        principalSchema: "customers",
                        principalTable: "store_customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_consents_store_customer_id_purpose",
                schema: "customers",
                table: "customer_consents",
                columns: new[] { "store_customer_id", "purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_consents",
                schema: "customers");
        }
    }
}
