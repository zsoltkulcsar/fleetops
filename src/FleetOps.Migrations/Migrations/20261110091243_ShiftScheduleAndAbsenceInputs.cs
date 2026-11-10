using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ShiftScheduleAndAbsenceInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absences",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    half_day = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_absences", x => x.id);
                    table.CheckConstraint("ck_absences_dates", "\"from\" <= \"to\" AND (half_day IS NULL OR \"from\" = \"to\")");
                    table.ForeignKey(
                        name: "FK_absences_employees_decided_by",
                        column: x => x.decided_by,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_absences_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    maintenance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.id);
                    table.CheckConstraint("ck_assignments_dates", "\"to\" IS NULL OR \"to\" >= \"from\"");
                    table.ForeignKey(
                        name: "FK_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignments_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleetops",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_absences_decided_by",
                schema: "fleetops",
                table: "absences",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "IX_absences_employee_id_from_to",
                schema: "fleetops",
                table: "absences",
                columns: new[] { "employee_id", "from", "to" });

            migrationBuilder.CreateIndex(
                name: "IX_assignments_employee_id_from",
                schema: "fleetops",
                table: "assignments",
                columns: new[] { "employee_id", "from" });

            migrationBuilder.CreateIndex(
                name: "IX_assignments_vehicle_id",
                schema: "fleetops",
                table: "assignments",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absences",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "assignments",
                schema: "fleetops");
        }
    }
}
