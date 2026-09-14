using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EcoWattCasa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    mqtt_topic = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    location = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nominal_watts = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "imported_bills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reading_from = table.Column<DateOnly>(type: "date", nullable: false),
                    reading_to = table.Column<DateOnly>(type: "date", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    kwh = table.Column<double>(type: "double precision", nullable: false),
                    meter_start = table.Column<double>(type: "double precision", nullable: false),
                    meter_end = table.Column<double>(type: "double precision", nullable: false),
                    basic_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_taxes = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imported_bills", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tariff_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    fixed_charge_per_day = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    source = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_schedules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "energy_readings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    watts = table.Column<double>(type: "double precision", nullable: false),
                    voltage = table.Column<double>(type: "double precision", nullable: false),
                    amperage = table.Column<double>(type: "double precision", nullable: false),
                    total_kwh = table.Column<double>(type: "double precision", nullable: true),
                    today_kwh = table.Column<double>(type: "double precision", nullable: true),
                    power_factor = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_energy_readings", x => x.id);
                    table.ForeignKey(
                        name: "fk_energy_readings_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tariff_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tariff_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    up_to_kwh = table.Column<double>(type: "double precision", nullable: true),
                    price_per_kwh = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_blocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tariff_blocks_tariff_schedules_tariff_schedule_id",
                        column: x => x.tariff_schedule_id,
                        principalTable: "tariff_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tariff_period_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tariff_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_period_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_tariff_period_charges_tariff_schedules_tariff_schedule_id",
                        column: x => x.tariff_schedule_id,
                        principalTable: "tariff_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tariff_surcharges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tariff_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(8,5)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_surcharges", x => x.id);
                    table.ForeignKey(
                        name: "fk_tariff_surcharges_tariff_schedules_tariff_schedule_id",
                        column: x => x.tariff_schedule_id,
                        principalTable: "tariff_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devices_mqtt_topic",
                table: "devices",
                column: "mqtt_topic",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_energy_readings_device_timestamp",
                table: "energy_readings",
                columns: new[] { "device_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_energy_readings_timestamp",
                table: "energy_readings",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_imported_bills_invoice_number",
                table: "imported_bills",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tariff_blocks_tariff_schedule_id_order",
                table: "tariff_blocks",
                columns: new[] { "tariff_schedule_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tariff_period_charges_tariff_schedule_id",
                table: "tariff_period_charges",
                column: "tariff_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_tariff_schedules_valid_from",
                table: "tariff_schedules",
                column: "valid_from",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tariff_surcharges_tariff_schedule_id",
                table: "tariff_surcharges",
                column: "tariff_schedule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "energy_readings");

            migrationBuilder.DropTable(
                name: "imported_bills");

            migrationBuilder.DropTable(
                name: "tariff_blocks");

            migrationBuilder.DropTable(
                name: "tariff_period_charges");

            migrationBuilder.DropTable(
                name: "tariff_surcharges");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "tariff_schedules");
        }
    }
}
