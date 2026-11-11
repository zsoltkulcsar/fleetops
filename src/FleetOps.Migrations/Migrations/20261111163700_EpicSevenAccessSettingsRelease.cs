using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicSevenAccessSettingsRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "override_id",
                schema: "fleetops",
                table: "shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pin_reset_requested_at",
                schema: "fleetops",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revoke_reason",
                schema: "fleetops",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_holidays",
                schema: "fleetops",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_holidays", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "setting_lists",
                schema: "fleetops",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    values = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_setting_lists", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_overrides",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    doc_types = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_overrides", x => x.id);
                    table.ForeignKey(
                        name: "FK_vehicle_overrides_employees_by",
                        column: x => x.by,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vehicle_overrides_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleetops",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shifts_override_id",
                schema: "fleetops",
                table: "shifts",
                column: "override_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_overrides_by",
                schema: "fleetops",
                table: "vehicle_overrides",
                column: "by");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_overrides_vehicle_id_valid_until",
                schema: "fleetops",
                table: "vehicle_overrides",
                columns: new[] { "vehicle_id", "valid_until" });

            migrationBuilder.AddForeignKey(
                name: "FK_shifts_vehicle_overrides_override_id",
                schema: "fleetops",
                table: "shifts",
                column: "override_id",
                principalSchema: "fleetops",
                principalTable: "vehicle_overrides",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shifts_vehicle_overrides_override_id",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropTable(
                name: "company_holidays",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "setting_lists",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "vehicle_overrides",
                schema: "fleetops");

            migrationBuilder.DropIndex(
                name: "IX_shifts_override_id",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "override_id",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "pin_reset_requested_at",
                schema: "fleetops",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "revoke_reason",
                schema: "fleetops",
                table: "sessions");
        }
    }
}
