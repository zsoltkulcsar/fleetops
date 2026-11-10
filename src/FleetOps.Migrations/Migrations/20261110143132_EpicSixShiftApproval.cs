using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicSixShiftApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_note",
                schema: "fleetops",
                table: "shifts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "fleetops",
                table: "shifts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                schema: "fleetops",
                table: "shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                schema: "fleetops",
                table: "shifts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval_note",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                schema: "fleetops",
                table: "shifts");
        }
    }
}
