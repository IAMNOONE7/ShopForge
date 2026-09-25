using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlansAndCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "plan_id",
                schema: "stores",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_stores = table.Column<int>(type: "integer", nullable: true),
                    max_products = table.Column<int>(type: "integer", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                    table.CheckConstraint("ck_plans_caps", "(max_stores IS NULL OR max_stores >= 0) AND (max_products IS NULL OR max_products >= 0)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_plan_id",
                schema: "stores",
                table: "tenants",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                schema: "stores",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_is_default",
                schema: "stores",
                table: "plans",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_plans_plan_id",
                schema: "stores",
                table: "tenants",
                column: "plan_id",
                principalSchema: "stores",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // The plans the platform sells. The uncapped one is the default, so introducing plans changes nothing
            // for a company until the platform puts it on a smaller one: no existing tenant wakes up over a cap.
            migrationBuilder.Sql(
                "INSERT INTO stores.plans (id, code, name, max_stores, max_products, is_default) VALUES "
                + "('01990000-0000-7000-8000-000000000001', 'starter', 'Starter', 1, 100, false), "
                + "('01990000-0000-7000-8000-000000000002', 'growth', 'Growth', 5, 2000, false), "
                + "('01990000-0000-7000-8000-000000000003', 'scale', 'Scale', NULL, NULL, true)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenants_plans_plan_id",
                schema: "stores",
                table: "tenants");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "stores");

            migrationBuilder.DropIndex(
                name: "ix_tenants_plan_id",
                schema: "stores",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "plan_id",
                schema: "stores",
                table: "tenants");
        }
    }
}
