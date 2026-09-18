using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EcoWattCasa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AthomHardware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "channel_index",
                table: "devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "min_relay_interval_seconds",
                table: "devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "relay_locked",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "relay_on",
                table: "devices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "relay_state_at",
                table: "devices",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "devices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "relay_commands",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_on = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relay_commands", x => x.id);
                    table.ForeignKey(
                        name: "fk_relay_commands_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devices_role",
                table: "devices",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "ix_relay_commands_device_created",
                table: "relay_commands",
                columns: new[] { "device_id", "created_at" });

            // ---------- Conversion de los datos existentes ----------
            //
            // Antes de esta migracion los tipos eran SonoffPowR2 / Esp32Sct013 / Simulated, que
            // correspondian al hardware que se habia evaluado y finalmente no se compro. Los
            // dispositivos que hay en la base son los tres simulados del mock, y hay decenas de
            // miles de lecturas colgando de sus ids: por eso se actualizan las filas en lugar de
            // borrarlas y recrearlas, incluido el renombre de topic al esquema nuevo.
            migrationBuilder.Sql("""
                UPDATE devices SET
                    type = 'AthomPlugV3',
                    role = 'Appliance',
                    channel_index = 0,
                    min_relay_interval_seconds = 60
                WHERE role = '';
                """);

            // Los topics viejos nombraban un hardware que no es el que llego.
            migrationBuilder.Sql("""
                UPDATE devices SET mqtt_topic = 'plug-pc',         name = 'PC + monitores' WHERE mqtt_topic = 'sonoff-pc';
                UPDATE devices SET mqtt_topic = 'plug-heladera',   name = 'Heladera'       WHERE mqtt_topic = 'sonoff-heladera';
                UPDATE devices SET mqtt_topic = 'plug-lavarropas', name = 'Lavarropas'     WHERE mqtt_topic = 'sonoff-lavarropas';
                """);

            // La heladera arranca con el rele bloqueado y con diez minutos minimos entre
            // conmutaciones, para que ni un clic repetido pueda ciclar el compresor.
            migrationBuilder.Sql("""
                UPDATE devices SET relay_locked = TRUE, min_relay_interval_seconds = 600
                WHERE mqtt_topic = 'plug-heladera';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relay_commands");

            migrationBuilder.DropIndex(
                name: "ix_devices_role",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "channel_index",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "min_relay_interval_seconds",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "relay_locked",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "relay_on",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "relay_state_at",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "role",
                table: "devices");
        }
    }
}
