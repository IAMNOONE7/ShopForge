using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarrierServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "carrier_service_key",
                schema: "orders",
                table: "shipping_methods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parcel_longest_mm",
                schema: "orders",
                table: "shipping_methods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parcel_max_weight_grams",
                schema: "orders",
                table: "shipping_methods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parcel_middle_mm",
                schema: "orders",
                table: "shipping_methods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parcel_shortest_mm",
                schema: "orders",
                table: "shipping_methods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pricing",
                schema: "orders",
                table: "shipping_methods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Flat");

            migrationBuilder.CreateTable(
                name: "carrier_services",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    max_weight_grams = table.Column<int>(type: "integer", nullable: false),
                    longest_mm = table.Column<int>(type: "integer", nullable: false),
                    middle_mm = table.Column<int>(type: "integer", nullable: false),
                    shortest_mm = table.Column<int>(type: "integer", nullable: false),
                    max_sum_of_sides_mm = table.Column<int>(type: "integer", nullable: true),
                    min_longest_mm = table.Column<int>(type: "integer", nullable: false),
                    min_middle_mm = table.Column<int>(type: "integer", nullable: false),
                    min_shortest_mm = table.Column<int>(type: "integer", nullable: false),
                    largest_volume_mm3 = table.Column<long>(type: "bigint", nullable: false),
                    max_parcels = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_services", x => x.id);
                    table.CheckConstraint("ck_carrier_services_limits", "max_weight_grams > 0 AND largest_volume_mm3 > 0 AND (max_parcels IS NULL OR max_parcels >= 1) AND longest_mm >= middle_mm AND middle_mm >= shortest_mm AND min_longest_mm >= min_middle_mm AND min_middle_mm >= min_shortest_mm AND min_shortest_mm > 0 AND min_longest_mm <= longest_mm AND min_middle_mm <= middle_mm AND min_shortest_mm <= shortest_mm");
                });

            migrationBuilder.CreateTable(
                name: "shipping_settings",
                schema: "orders",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    packaging_weight_grams = table.Column<int>(type: "integer", nullable: false),
                    fill_percent = table.Column<int>(type: "integer", nullable: false),
                    largest_parcel_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipping_settings", x => x.store_id);
                    table.CheckConstraint("ck_shipping_settings_values", "packaging_weight_grams >= 0 AND fill_percent BETWEEN 1 AND 100 AND largest_parcel_order >= 1");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipping_methods_parcel_limits",
                schema: "orders",
                table: "shipping_methods",
                sql: "(parcel_max_weight_grams IS NULL OR parcel_max_weight_grams > 0) AND ((parcel_longest_mm IS NULL AND parcel_middle_mm IS NULL AND parcel_shortest_mm IS NULL) OR (parcel_longest_mm >= parcel_middle_mm AND parcel_middle_mm >= parcel_shortest_mm AND parcel_shortest_mm > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipping_methods_parcels",
                schema: "orders",
                table: "shipping_methods",
                sql: "(pricing = 'PerParcel' AND carrier_service_key IS NOT NULL) OR (pricing = 'Flat' AND parcel_max_weight_grams IS NULL AND parcel_longest_mm IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_services_key",
                schema: "orders",
                table: "carrier_services",
                column: "key",
                unique: true);

            // The services V28a read from Packeta's own pages (D-214): a Czech Z-BOX, one parcel to an order until Packeta
            // says more may go to one box (A20), and a Czech doorstep, whose largest parcel is the largest box its rule on
            // the sides allows, a 40 cm cube.
            migrationBuilder.Sql(
                "INSERT INTO orders.carrier_services (id, key, provider_key, name, kind, country, is_active, max_weight_grams, "
                + "longest_mm, middle_mm, shortest_mm, max_sum_of_sides_mm, min_longest_mm, min_middle_mm, min_shortest_mm, "
                + "largest_volume_mm3, max_parcels, source, source_date) VALUES "
                + "('019a1b00-0000-7000-8000-000000000001', 'packeta-cz-zbox', 'packeta', 'Packeta Z-BOX, Czechia', 'Locker', 'CZ', true, 15000, "
                + "600, 430, 350, NULL, 100, 70, 10, 90300000, 1, "
                + "'docs.packeta.com/docs/destination-country/cz/z-box, page updated May 2026: up to 15 kg in compartments up to "
                + "35 x 43 x 60 cm, at least 10 x 7 x 1 cm. One parcel to an order until Packeta confirms more may go to one box (A20).', "
                + "'2026-10-24'), "
                + "('019a1b00-0000-7000-8000-000000000002', 'packeta-cz-home', 'packeta', 'Packeta home delivery, Czechia', 'Home', 'CZ', true, 5000, "
                + "700, 700, 700, 1200, 100, 70, 10, 64000000, NULL, "
                + "'docs.packeta.com/docs/destination-country/cz/cz-home-delivery (carrier 106), page updated May 2026: up to 5 kg, "
                + "sides adding up to at most 120 cm with the longest at most 70 cm, at least 10 x 7 x 1 cm. Largest volume: the 40 cm cube that rule allows.', "
                + "'2026-10-24')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carrier_services",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "shipping_settings",
                schema: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_shipping_methods_parcel_limits",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropCheckConstraint(
                name: "ck_shipping_methods_parcels",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "carrier_service_key",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "parcel_longest_mm",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "parcel_max_weight_grams",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "parcel_middle_mm",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "parcel_shortest_mm",
                schema: "orders",
                table: "shipping_methods");

            migrationBuilder.DropColumn(
                name: "pricing",
                schema: "orders",
                table: "shipping_methods");
        }
    }
}
