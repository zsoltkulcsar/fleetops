using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicEightAbsences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "vacation_opening_balance",
                schema: "fleetops",
                table: "employees",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vacation_opening_balance_year",
                schema: "fleetops",
                table: "employees",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "fleetops",
                table: "absences",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "command_id",
                schema: "fleetops",
                table: "absences",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decline_reason",
                schema: "fleetops",
                table: "absences",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "device_time",
                schema: "fleetops",
                table: "absences",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "fleetops",
                table: "absences",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "schedule_change_requests",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_from = table.Column<DateOnly>(type: "date", nullable: false),
                    proposed_to = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    decline_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_change_requests", x => x.id);
                    table.CheckConstraint("ck_schedule_request_dates", "proposed_from <= proposed_to");
                    table.ForeignKey(
                        name: "FK_schedule_change_requests_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "fleetops",
                        principalTable: "cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_schedule_change_requests_employees_decided_by",
                        column: x => x.decided_by,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_schedule_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_absences_employee_id_command_id",
                schema: "fleetops",
                table: "absences",
                columns: new[] { "employee_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_change_requests_cycle_id",
                schema: "fleetops",
                table: "schedule_change_requests",
                column: "cycle_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_change_requests_decided_by",
                schema: "fleetops",
                table: "schedule_change_requests",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_change_requests_employee_id_command_id",
                schema: "fleetops",
                table: "schedule_change_requests",
                columns: new[] { "employee_id", "command_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_change_requests",
                schema: "fleetops");

            migrationBuilder.DropIndex(
                name: "IX_absences_employee_id_command_id",
                schema: "fleetops",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "vacation_opening_balance",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "vacation_opening_balance_year",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "fleetops",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "command_id",
                schema: "fleetops",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "decline_reason",
                schema: "fleetops",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "device_time",
                schema: "fleetops",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "fleetops",
                table: "absences");
        }
    }
}
