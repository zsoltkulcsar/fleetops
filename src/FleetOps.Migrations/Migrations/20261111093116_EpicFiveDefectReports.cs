using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicFiveDefectReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "defect_reports",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    photos = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    device_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    server_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_defect_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_defect_reports_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleetops",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "defect_photos",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    defect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_defect_photos", x => x.id);
                    table.ForeignKey(
                        name: "FK_defect_photos_defect_reports_defect_id",
                        column: x => x.defect_id,
                        principalSchema: "fleetops",
                        principalTable: "defect_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_defect_photos_defect_id_sort_order",
                schema: "fleetops",
                table: "defect_photos",
                columns: new[] { "defect_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_defect_photos_object_key",
                schema: "fleetops",
                table: "defect_photos",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_defect_reports_command_id",
                schema: "fleetops",
                table: "defect_reports",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_defect_reports_vehicle_id_status",
                schema: "fleetops",
                table: "defect_reports",
                columns: new[] { "vehicle_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "defect_photos",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "defect_reports",
                schema: "fleetops");
        }
    }
}
