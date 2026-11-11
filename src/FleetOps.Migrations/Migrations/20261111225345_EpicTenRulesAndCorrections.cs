using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicTenRulesAndCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compliance_rules",
                schema: "fleetops",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GuideValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Value = table.Column<string>(type: "jsonb", nullable: true),
                    ValueSchema = table.Column<string>(type: "jsonb", nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "correction_requests",
                schema: "fleetops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProposedStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProposedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequiresHr = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: false),
                    Diffs = table.Column<string>(type: "jsonb", nullable: false),
                    DeclineReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeviceTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_correction_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_correction_requests_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "fleetops",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shift_rule_guidance",
                schema: "fleetops",
                columns: table => new
                {
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Warnings = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_rule_guidance", x => x.ShiftId);
                    table.ForeignKey(
                        name: "FK_shift_rule_guidance_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "fleetops",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rule_confirmations",
                schema: "fleetops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(16)", nullable: false),
                    Value = table.Column<string>(type: "jsonb", nullable: false),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_confirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rule_confirmations_compliance_rules_RuleId",
                        column: x => x.RuleId,
                        principalSchema: "fleetops",
                        principalTable: "compliance_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "fleetops",
                table: "compliance_rules",
                columns: new[] { "Id", "Confirmed", "GuideValue", "Name", "Value", "ValueSchema" },
                values: new object[,]
                {
                    { "C-01", false, "9 h; 10 h allowed max. 2× per week", "Max daily driving", "{\"minutes\":540,\"extendedMinutes\":600,\"extensionsPerWeek\":2}", "{\"minutes\":540,\"extendedMinutes\":600,\"extensionsPerWeek\":2}" },
                    { "C-02", false, "56 h", "Max weekly driving", "{\"minutes\":3360}", "{\"minutes\":3360}" },
                    { "C-03", false, "90 h", "Max driving in 2 consecutive weeks", "{\"minutes\":5400}", "{\"minutes\":5400}" },
                    { "C-04", false, "≥45 min after 4 h 30 min driving (split 15 + 30 allowed — verify)", "Break after driving", "{\"drivingMinutes\":270,\"breakMinutes\":45,\"splitFirstMinutes\":15,\"splitLastMinutes\":30}", "{\"drivingMinutes\":270,\"breakMinutes\":45,\"splitFirstMinutes\":15,\"splitLastMinutes\":30}" },
                    { "C-05", false, "≥11 h; may reduce to ≥9 h max. 3× between weekly rests", "Daily rest", "{\"minutes\":660,\"reducedMinutes\":540,\"reductions\":3}", "{\"minutes\":660,\"reducedMinutes\":540,\"reductions\":3}" },
                    { "C-06", false, "≥45 h regular; ≥24 h reduced (compensation rules apply)", "Weekly rest", null, "{\"minutes\":2700,\"reducedMinutes\":1440}" },
                    { "C-07", false, "30 min if >6 h work; 45 min if >9 h", "Working-time breaks", "{\"workMinutes\":360,\"breakMinutes\":30,\"longWorkMinutes\":540,\"longBreakMinutes\":45}", "{\"workMinutes\":360,\"breakMinutes\":30,\"longWorkMinutes\":540,\"longBreakMinutes\":45}" },
                    { "C-08", false, "max 60 h in a week, average ≤48 h; reference period NEEDS_CONFIRMATION (4 months standard)", "Weekly working time (mobile workers)", null, "{\"minutes\":3600,\"averageMinutes\":2880}" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_correction_requests_EmployeeId_CommandId",
                schema: "fleetops",
                table: "correction_requests",
                columns: new[] { "EmployeeId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_correction_requests_EmployeeId_Date",
                schema: "fleetops",
                table: "correction_requests",
                columns: new[] { "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_correction_requests_ShiftId",
                schema: "fleetops",
                table: "correction_requests",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_rule_confirmations_RuleId",
                schema: "fleetops",
                table: "rule_confirmations",
                column: "RuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correction_requests",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "rule_confirmations",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "shift_rule_guidance",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "compliance_rules",
                schema: "fleetops");
        }
    }
}
