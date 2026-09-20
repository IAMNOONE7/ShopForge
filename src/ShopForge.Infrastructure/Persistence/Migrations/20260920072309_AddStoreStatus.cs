using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "stores",
                table: "stores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Draft");

            // Stores created before publishing existed were already serving their domains.
            migrationBuilder.Sql("UPDATE stores.stores SET status = 'Published';");
            migrationBuilder.Sql("ALTER TABLE stores.stores ALTER COLUMN status DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                schema: "stores",
                table: "stores");
        }
    }
}
