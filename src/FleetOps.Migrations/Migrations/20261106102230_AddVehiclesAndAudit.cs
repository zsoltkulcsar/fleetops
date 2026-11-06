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
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                schema: "fleetops",
                table: "users");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "fleetops",
                table: "sessions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "fleetops",
                table: "privacy_acknowledgements",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "fleetops",
                table: "password_reset_tokens",
                newName: "id");

            migrationBuilder.AddColumn<int>(
                name: "failed_second_factor_attempts",
                schema: "fleetops",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "text", nullable: false),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
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
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    year = table.Column<int>(type: "integer", nullable: false),
                    gross_weight_kg = table.Column<int>(type: "integer", nullable: false),
                    required_licence_class = table.Column<string>(type: "text", nullable: true),
                    ownership = table.Column<string>(type: "text", nullable: false),
                    lease_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    trailer_type = table.Column<string>(type: "text", nullable: true),
                    odometer_km = table.Column<int>(type: "integer", nullable: true),
                    odometer_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    depot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_interval_km = table.Column<int>(type: "integer", nullable: true),
                    service_interval_months = table.Column<int>(type: "integer", nullable: true),
                    last_tacho_download_at = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicles", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "fleetops",
                table: "depots",
                columns: new[] { "id", "address", "name" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "Default address", "Default depot" });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "fleetops",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_user_id",
                schema: "fleetops",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_privacy_acknowledgements_employee_id",
                schema: "fleetops",
                table: "privacy_acknowledgements",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_user_id",
                schema: "fleetops",
                table: "password_reset_tokens",
                column: "user_id");

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

            migrationBuilder.AddForeignKey(
                name: "FK_password_reset_tokens_users_user_id",
                schema: "fleetops",
                table: "password_reset_tokens",
                column: "user_id",
                principalSchema: "fleetops",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_privacy_acknowledgements_employees_employee_id",
                schema: "fleetops",
                table: "privacy_acknowledgements",
                column: "employee_id",
                principalSchema: "fleetops",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_sessions_users_user_id",
                schema: "fleetops",
                table: "sessions",
                column: "user_id",
                principalSchema: "fleetops",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("GRANT SELECT, INSERT ON fleetops.audit_entries TO fleetops_runtime; REVOKE UPDATE, DELETE, TRUNCATE ON fleetops.audit_entries FROM fleetops_runtime; GRANT SELECT, INSERT ON fleetops.vehicle_plate_history TO fleetops_runtime; REVOKE UPDATE, DELETE, TRUNCATE ON fleetops.vehicle_plate_history FROM fleetops_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_password_reset_tokens_users_user_id",
                schema: "fleetops",
                table: "password_reset_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_privacy_acknowledgements_employees_employee_id",
                schema: "fleetops",
                table: "privacy_acknowledgements");

            migrationBuilder.DropForeignKey(
                name: "FK_sessions_users_user_id",
                schema: "fleetops",
                table: "sessions");

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

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                schema: "fleetops",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_sessions_user_id",
                schema: "fleetops",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "IX_privacy_acknowledgements_employee_id",
                schema: "fleetops",
                table: "privacy_acknowledgements");

            migrationBuilder.DropIndex(
                name: "IX_password_reset_tokens_user_id",
                schema: "fleetops",
                table: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "failed_second_factor_attempts",
                schema: "fleetops",
                table: "sessions");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "fleetops",
                table: "sessions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "fleetops",
                table: "privacy_acknowledgements",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "fleetops",
                table: "password_reset_tokens",
                newName: "Id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "fleetops",
                table: "users",
                column: "normalized_email");
        }
    }
}
