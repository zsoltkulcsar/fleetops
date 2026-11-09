using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SyncD31a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "personnel_number",
                schema: "fleetops",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "fleetops",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "enrolment_codes",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrolment_codes", x => x.id);
                    table.ForeignKey(
                        name: "FK_enrolment_codes_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employees_personnel_number",
                schema: "fleetops",
                table: "employees",
                column: "personnel_number",
                unique: true,
                filter: "personnel_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_enrolment_codes_employee_id_expires_at",
                schema: "fleetops",
                table: "enrolment_codes",
                columns: new[] { "employee_id", "expires_at" },
                filter: "used_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrolment_codes",
                schema: "fleetops");

            migrationBuilder.DropIndex(
                name: "IX_employees_personnel_number",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "personnel_number",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "fleetops",
                table: "employees");
        }
    }
}
