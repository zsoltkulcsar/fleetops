using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fleetops");

            migrationBuilder.CreateTable(
                name: "migration_marker",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_marker", x => x.id);
                });

            migrationBuilder.InsertData(table: "migration_marker", columns: new[] { "id", "name" }, values: new object[] { 1, "solution-skeleton" }, schema: "fleetops");
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA fleetops TO fleetops_runtime; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA fleetops TO fleetops_runtime; ALTER DEFAULT PRIVILEGES FOR ROLE fleetops_migrator IN SCHEMA fleetops GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fleetops_runtime; REVOKE CREATE ON SCHEMA fleetops FROM fleetops_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "migration_marker",
                schema: "fleetops");
        }
    }
}
