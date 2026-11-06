using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddVehiclesAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "depots",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_depots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_plate_history",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plate = table.Column<string>(type: "text", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_plate_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    plate = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    vin = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    make = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    GrossWeightKg = table.Column<int>(type: "integer", nullable: false),
                    RequiredLicenceClass = table.Column<string>(type: "text", nullable: true),
                    ownership = table.Column<string>(type: "text", nullable: false),
                    LeaseEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TrailerType = table.Column<int>(type: "integer", nullable: true),
                    OdometerKm = table.Column<int>(type: "integer", nullable: true),
                    OdometerUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DepotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceIntervalKm = table.Column<int>(type: "integer", nullable: true),
                    ServiceIntervalMonths = table.Column<int>(type: "integer", nullable: true),
                    LastTachoDownloadAt = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_plate",
                schema: "fleetops",
                table: "vehicles",
                column: "plate",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_vin",
                schema: "fleetops",
                table: "vehicles",
                column: "vin",
                unique: true,
                filter: "status = 'Active'");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON fleetops.audit_entries TO fleetops_runtime; REVOKE UPDATE, DELETE, TRUNCATE ON fleetops.audit_entries FROM fleetops_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "depots",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "vehicle_plate_history",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "vehicles",
                schema: "fleetops");
        }
    }
}
