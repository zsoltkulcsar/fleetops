using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicSevenLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "leave_actor_id",
                schema: "fleetops",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "leave_reason",
                schema: "fleetops",
                table: "employees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "leave_shift_end",
                schema: "fleetops",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "leave_shift_reason",
                schema: "fleetops",
                table: "employees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "leave_actor_id",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "leave_reason",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "leave_shift_end",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "leave_shift_reason",
                schema: "fleetops",
                table: "employees");
        }
    }
}
