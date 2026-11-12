using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicElevenPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "export_id",
                schema: "fleetops",
                table: "shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_exports",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    correction_of = table.Column<Guid>(type: "uuid", nullable: true),
                    shift_versions = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    deltas = table.Column<string>(type: "jsonb", nullable: false),
                    file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_exports", x => x.id);
                    table.CheckConstraint("ck_payroll_kind", "kind IN ('regular', 'supplement', 'correction')");
                    table.CheckConstraint("ck_payroll_period", "period_to >= period_from AND version > 0");
                    table.CheckConstraint("ck_payroll_status", "status IN ('generated', 'sent', 'discarded', 'superseded')");
                    table.ForeignKey(
                        name: "FK_payroll_exports_employees_created_by",
                        column: x => x.created_by,
                        principalSchema: "fleetops",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payroll_exports_payroll_exports_correction_of",
                        column: x => x.correction_of,
                        principalSchema: "fleetops",
                        principalTable: "payroll_exports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shifts_export_id",
                schema: "fleetops",
                table: "shifts",
                column: "export_id");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_exports_correction_of",
                schema: "fleetops",
                table: "payroll_exports",
                column: "correction_of");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_exports_created_by",
                schema: "fleetops",
                table: "payroll_exports",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_exports_period_from_period_to_version",
                schema: "fleetops",
                table: "payroll_exports",
                columns: new[] { "period_from", "period_to", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_shifts_payroll_exports_export_id",
                schema: "fleetops",
                table: "shifts",
                column: "export_id",
                principalSchema: "fleetops",
                principalTable: "payroll_exports",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shifts_payroll_exports_export_id",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropTable(
                name: "payroll_exports",
                schema: "fleetops");

            migrationBuilder.DropIndex(
                name: "IX_shifts_export_id",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "export_id",
                schema: "fleetops",
                table: "shifts");
        }
    }
}
