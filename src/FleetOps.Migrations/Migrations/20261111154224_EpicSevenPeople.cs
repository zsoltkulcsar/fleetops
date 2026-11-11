using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicSevenPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "adr_required",
                schema: "fleetops",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "depot_id",
                schema: "fleetops",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                schema: "fleetops",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "full_time");

            migrationBuilder.AddColumn<string>(
                name: "language_pref",
                schema: "fleetops",
                table: "employees",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "de");

            migrationBuilder.AddColumn<decimal>(
                name: "vacation_entitlement_days",
                schema: "fleetops",
                table: "employees",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_depot_id",
                schema: "fleetops",
                table: "employees",
                column: "depot_id");

            migrationBuilder.AddForeignKey(
                name: "FK_employees_depots_depot_id",
                schema: "fleetops",
                table: "employees",
                column: "depot_id",
                principalSchema: "fleetops",
                principalTable: "depots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employees_depots_depot_id",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "IX_employees_depot_id",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "adr_required",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "depot_id",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "employment_type",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "language_pref",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "vacation_entitlement_days",
                schema: "fleetops",
                table: "employees");
        }
    }
}
