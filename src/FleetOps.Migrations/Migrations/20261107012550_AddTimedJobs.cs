using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTimedJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_checkpoints",
                schema: "fleetops",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    cursor = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_checkpoints", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    trigger = table.Column<string>(type: "text", nullable: false),
                    actor_name = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    processed = table.Column<int>(type: "integer", nullable: false),
                    error_type = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_name_started_at",
                schema: "fleetops",
                table: "job_runs",
                columns: new[] { "name", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_checkpoints",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "job_runs",
                schema: "fleetops");
        }
    }
}
