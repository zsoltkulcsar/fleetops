using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cycle_pattern",
                schema: "fleetops",
                table: "employees",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cycle_pattern_version",
                schema: "fleetops",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "cycles",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pattern_slot = table.Column<int>(type: "integer", nullable: true),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycles", x => x.id);
                    table.CheckConstraint("ck_cycles_dates", "\"from\" <= \"to\"");
                    table.CheckConstraint("ck_cycles_pattern_slot", "pattern_slot IS NULL OR pattern_slot >= 0");
                    table.CheckConstraint("ck_cycles_source", "source IN ('Generated', 'Manual') AND (source <> 'Generated' OR pattern_slot IS NOT NULL)");
                    table.CheckConstraint("ck_cycles_status", "status IN ('Planned', 'Active', 'Overrun', 'Done', 'Cancelled')");
                    table.CheckConstraint("ck_cycles_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_cycles_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycles_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleetops",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cycles_employee_id_pattern_slot",
                schema: "fleetops",
                table: "cycles",
                columns: new[] { "employee_id", "pattern_slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cycles_vehicle_id",
                schema: "fleetops",
                table: "cycles",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cycles",
                schema: "fleetops");

            migrationBuilder.DropColumn(
                name: "cycle_pattern",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "cycle_pattern_version",
                schema: "fleetops",
                table: "employees");
        }
    }
}
