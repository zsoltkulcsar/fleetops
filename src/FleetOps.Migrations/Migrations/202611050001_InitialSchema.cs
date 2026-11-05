using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace FleetOps.Migrations.Migrations;

public partial class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("fleetops");
        migrationBuilder.CreateTable("migration_marker", columns: table => new { Id = table.Column<int>(nullable: false), name = table.Column<string>(maxLength: 100, nullable: false) }, schema: "fleetops", constraints: table => table.PrimaryKey("PK_migration_marker", x => x.Id));
        migrationBuilder.InsertData(table: "migration_marker", columns: new[] { "Id", "name" }, values: new object[] { 1, "solution-skeleton" }, schema: "fleetops");
        migrationBuilder.Sql("GRANT USAGE ON SCHEMA fleetops TO fleetops_runtime; GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA fleetops TO fleetops_runtime; ALTER DEFAULT PRIVILEGES FOR ROLE fleetops_migrator IN SCHEMA fleetops GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fleetops_runtime; REVOKE CREATE ON SCHEMA fleetops FROM fleetops_runtime;");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("migration_marker", "fleetops");
}
