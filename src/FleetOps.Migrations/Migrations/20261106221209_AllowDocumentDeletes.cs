using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AllowDocumentDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("GRANT DELETE ON fleetops.documents, fleetops.document_files, fleetops.pending_files TO fleetops_runtime;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE DELETE ON fleetops.documents, fleetops.document_files, fleetops.pending_files FROM fleetops_runtime;");

        }
    }
}
