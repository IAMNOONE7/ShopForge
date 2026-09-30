using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockPerVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_movements_tenant_id_product_id_occurred_at",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_return_lines",
                schema: "orders",
                table: "order_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_inventory_items_warehouse_id_product_id",
                schema: "inventory",
                table: "inventory_items");

            migrationBuilder.DropPrimaryKey(
                name: "pk_cart_lines",
                schema: "orders",
                table: "cart_lines");

            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "inventory",
                table: "stock_reservations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                schema: "inventory",
                table: "stock_reservations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "inventory",
                table: "stock_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                schema: "inventory",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                schema: "orders",
                table: "order_return_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                schema: "orders",
                table: "order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "inventory",
                table: "inventory_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                schema: "inventory",
                table: "inventory_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                schema: "orders",
                table: "cart_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));


            // Everything that counted a product now counts the one form it had. The joins run before the new keys and
            // indexes are built, because until they do every row carries the same empty id and the unique index over
            // (warehouse, variant) would refuse to be created at all.
            migrationBuilder.Sql(
                """
                UPDATE inventory.inventory_items i
                SET variant_id = v.id
                FROM catalog.product_variants v
                WHERE v.product_id = i.product_id AND v.position = 0
                """);

            // A movement and a reservation name the same shelf the item does.
            migrationBuilder.Sql(
                """
                UPDATE inventory.stock_movements m
                SET variant_id = v.id
                FROM catalog.product_variants v
                WHERE v.product_id = m.product_id AND v.position = 0
                """);

            migrationBuilder.Sql(
                """
                UPDATE inventory.stock_reservations r
                SET variant_id = v.id
                FROM catalog.product_variants v
                WHERE v.product_id = r.product_id AND v.position = 0
                """);

            // A line of a cart, an order or a return names a listing, and a listing sells one product, which until now
            // had exactly one form.
            migrationBuilder.Sql(
                """
                UPDATE orders.cart_lines l
                SET variant_id = v.id
                FROM catalog.store_products sp
                JOIN catalog.product_variants v ON v.product_id = sp.product_id AND v.position = 0
                WHERE sp.id = l.store_product_id
                """);

            migrationBuilder.Sql(
                """
                UPDATE orders.order_lines l
                SET variant_id = v.id
                FROM catalog.store_products sp
                JOIN catalog.product_variants v ON v.product_id = sp.product_id AND v.position = 0
                WHERE sp.id = l.store_product_id
                """);

            migrationBuilder.Sql(
                """
                UPDATE orders.order_return_lines l
                SET variant_id = v.id
                FROM catalog.store_products sp
                JOIN catalog.product_variants v ON v.product_id = sp.product_id AND v.position = 0
                WHERE sp.id = l.store_product_id
                """);

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_return_lines",
                schema: "orders",
                table: "order_return_lines",
                columns: new[] { "order_return_id", "store_product_id", "variant_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_cart_lines",
                schema: "orders",
                table: "cart_lines",
                columns: new[] { "cart_id", "store_product_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_variant_id_occurred_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "variant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_warehouse_id_variant_id",
                schema: "inventory",
                table: "inventory_items",
                columns: new[] { "warehouse_id", "variant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_movements_tenant_id_variant_id_occurred_at",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_return_lines",
                schema: "orders",
                table: "order_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_inventory_items_warehouse_id_variant_id",
                schema: "inventory",
                table: "inventory_items");

            migrationBuilder.DropPrimaryKey(
                name: "pk_cart_lines",
                schema: "orders",
                table: "cart_lines");

            migrationBuilder.DropColumn(
                name: "variant_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "variant_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "variant_id",
                schema: "orders",
                table: "order_return_lines");

            migrationBuilder.DropColumn(
                name: "variant_id",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "variant_id",
                schema: "inventory",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "variant_id",
                schema: "orders",
                table: "cart_lines");

            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "inventory",
                table: "stock_reservations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "inventory",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "inventory",
                table: "inventory_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_return_lines",
                schema: "orders",
                table: "order_return_lines",
                columns: new[] { "order_return_id", "store_product_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_cart_lines",
                schema: "orders",
                table: "cart_lines",
                columns: new[] { "cart_id", "store_product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_product_id_occurred_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "product_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_warehouse_id_product_id",
                schema: "inventory",
                table: "inventory_items",
                columns: new[] { "warehouse_id", "product_id" },
                unique: true);
        }
    }
}
