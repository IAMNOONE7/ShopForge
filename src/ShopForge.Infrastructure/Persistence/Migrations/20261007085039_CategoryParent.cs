using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategoryParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                schema: "catalog",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_store_id_parent_id",
                schema: "catalog",
                table: "categories",
                columns: new[] { "store_id", "parent_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_categories_categories_store_id_parent_id",
                schema: "catalog",
                table: "categories",
                columns: new[] { "store_id", "parent_id" },
                principalSchema: "catalog",
                principalTable: "categories",
                principalColumns: new[] { "store_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_categories_categories_store_id_parent_id",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ix_categories_store_id_parent_id",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "parent_id",
                schema: "catalog",
                table: "categories");
        }
    }
}
