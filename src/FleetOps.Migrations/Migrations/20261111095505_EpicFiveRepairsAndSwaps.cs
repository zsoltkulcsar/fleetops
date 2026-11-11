using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicFiveRepairsAndSwaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workshop = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    scheduled_for = table.Column<DateOnly>(type: "date", nullable: false),
                    defect_report_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    done_at = table.Column<DateOnly>(type: "date", nullable: true),
                    odometer_at_done_km = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance", x => x.id);
                    table.CheckConstraint("ck_maintenance_odometer", "odometer_at_done_km IS NULL OR odometer_at_done_km >= 0");
                    table.CheckConstraint("ck_maintenance_type", "type = 'repair'");
                    table.ForeignKey(
                        name: "FK_maintenance_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleetops",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_defect_reports_repair_id",
                schema: "fleetops",
                table: "defect_reports",
                column: "repair_id");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_maintenance_id",
                schema: "fleetops",
                table: "assignments",
                column: "maintenance_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_vehicle_id_scheduled_for",
                schema: "fleetops",
                table: "maintenance",
                columns: new[] { "vehicle_id", "scheduled_for" });

            migrationBuilder.AddForeignKey(
                name: "FK_assignments_maintenance_maintenance_id",
                schema: "fleetops",
                table: "assignments",
                column: "maintenance_id",
                principalSchema: "fleetops",
                principalTable: "maintenance",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_defect_reports_maintenance_repair_id",
                schema: "fleetops",
                table: "defect_reports",
                column: "repair_id",
                principalSchema: "fleetops",
                principalTable: "maintenance",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_assignments_maintenance_maintenance_id",
                schema: "fleetops",
                table: "assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_defect_reports_maintenance_repair_id",
                schema: "fleetops",
                table: "defect_reports");

            migrationBuilder.DropTable(
                name: "maintenance",
                schema: "fleetops");

            migrationBuilder.DropIndex(
                name: "IX_defect_reports_repair_id",
                schema: "fleetops",
                table: "defect_reports");

            migrationBuilder.DropIndex(
                name: "IX_assignments_maintenance_id",
                schema: "fleetops",
                table: "assignments");
        }
    }
}
