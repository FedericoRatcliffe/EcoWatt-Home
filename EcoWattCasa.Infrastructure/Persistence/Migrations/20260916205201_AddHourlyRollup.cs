using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcoWattCasa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHourlyRollup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "energy_hourly",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    first_total_kwh = table.Column<double>(type: "double precision", nullable: true),
                    last_total_kwh = table.Column<double>(type: "double precision", nullable: true),
                    avg_watts = table.Column<double>(type: "double precision", nullable: false),
                    max_watts = table.Column<double>(type: "double precision", nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: false),
                    first_timestamp = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_timestamp = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    rolled_up_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_energy_hourly", x => new { x.device_id, x.hour_utc });
                    table.ForeignKey(
                        name: "fk_energy_hourly_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_energy_hourly_hour",
                table: "energy_hourly",
                column: "hour_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "energy_hourly");
        }
    }
}
