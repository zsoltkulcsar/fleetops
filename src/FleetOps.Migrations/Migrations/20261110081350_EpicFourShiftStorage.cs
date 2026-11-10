using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicFourShiftStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "command_receipts",
                schema: "fleetops",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    base_version = table.Column<int>(type: "integer", nullable: false),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    server_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_receipts", x => new { x.employee_id, x.command_id });
                    table.ForeignKey(
                        name: "FK_command_receipts_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    truck_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trailer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    odometer_start_km = table.Column<int>(type: "integer", nullable: true),
                    odometer_end_km = table.Column<int>(type: "integer", nullable: true),
                    departure_check_ok = table.Column<bool>(type: "boolean", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reject_reason = table.Column<string>(type: "text", nullable: true),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    server_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    device_time_gap = table.Column<bool>(type: "boolean", nullable: false),
                    blocked_at_start = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    last_office_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shifts", x => x.id);
                    table.CheckConstraint("ck_shifts_dates", "end_at IS NULL OR end_at >= start_at");
                    table.CheckConstraint("ck_shifts_odometer", "(odometer_start_km IS NULL OR odometer_start_km >= 0) AND (odometer_end_km IS NULL OR odometer_end_km >= COALESCE(odometer_start_km, 0))");
                    table.CheckConstraint("ck_shifts_running", "(status = 'Running' AND end_at IS NULL) OR (status <> 'Running' AND end_at IS NOT NULL)");
                    table.CheckConstraint("ck_shifts_status", "status IN ('Running', 'Draft', 'Submitted', 'Approved', 'Rejected', 'Exported')");
                    table.CheckConstraint("ck_shifts_version", "version > 0 AND last_office_version >= 0 AND last_office_version <= version");
                    table.ForeignKey(
                        name: "FK_shifts_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shifts_vehicles_trailer_id",
                        column: x => x.trailer_id,
                        principalSchema: "fleetops",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shifts_vehicles_truck_id",
                        column: x => x.truck_id,
                        principalSchema: "fleetops",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activities",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.id);
                    table.CheckConstraint("ck_activities_dates", "end_at IS NULL OR end_at >= start_at");
                    table.ForeignKey(
                        name: "FK_activities_shifts_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "fleetops",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "overnights",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    night = table.Column<DateOnly>(type: "date", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overnights", x => x.id);
                    table.ForeignKey(
                        name: "FK_overnights_shifts_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "fleetops",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shift_conflicts",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_version = table.Column<int>(type: "integer", nullable: false),
                    office_version = table.Column<int>(type: "integer", nullable: false),
                    driver_payload = table.Column<string>(type: "jsonb", nullable: false),
                    office_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "FK_shift_conflicts_shifts_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "fleetops",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_activities_one_running_per_shift",
                schema: "fleetops",
                table: "activities",
                column: "shift_id",
                unique: true,
                filter: "end_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_command_receipts_employee_id_aggregate_id_server_received_at",
                schema: "fleetops",
                table: "command_receipts",
                columns: new[] { "employee_id", "aggregate_id", "server_received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_overnights_shift_id_night",
                schema: "fleetops",
                table: "overnights",
                columns: new[] { "shift_id", "night" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shift_conflicts_employee_id_command_id",
                schema: "fleetops",
                table: "shift_conflicts",
                columns: new[] { "employee_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shift_conflicts_shift_id",
                schema: "fleetops",
                table: "shift_conflicts",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "IX_shifts_employee_id_date",
                schema: "fleetops",
                table: "shifts",
                columns: new[] { "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_shifts_trailer_id",
                schema: "fleetops",
                table: "shifts",
                column: "trailer_id");

            migrationBuilder.CreateIndex(
                name: "IX_shifts_truck_id",
                schema: "fleetops",
                table: "shifts",
                column: "truck_id");

            migrationBuilder.CreateIndex(
                name: "ux_shifts_one_running_per_employee",
                schema: "fleetops",
                table: "shifts",
                column: "employee_id",
                unique: true,
                filter: "status = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "command_receipts",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "overnights",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "shift_conflicts",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "shifts",
                schema: "fleetops");
        }
    }
}
