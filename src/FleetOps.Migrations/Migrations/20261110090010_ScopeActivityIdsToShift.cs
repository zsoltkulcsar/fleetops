using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ScopeActivityIdsToShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_activities",
                schema: "fleetops",
                table: "activities");

            migrationBuilder.AddPrimaryKey(
                name: "PK_activities",
                schema: "fleetops",
                table: "activities",
                columns: new[] { "shift_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_activities",
                schema: "fleetops",
                table: "activities");

            migrationBuilder.AddPrimaryKey(
                name: "PK_activities",
                schema: "fleetops",
                table: "activities",
                column: "id");
        }
    }
}
