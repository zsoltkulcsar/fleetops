using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: true),
                    issued_at = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_at = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "text", nullable: true),
                    replaces_id = table.Column<Guid>(type: "uuid", nullable: true),
                    categories = table.Column<string[]>(type: "text[]", nullable: false),
                    last_sight_check_at = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_reason = table.Column<string>(type: "text", nullable: true),
                    import_attested = table.Column<bool>(type: "boolean", nullable: false),
                    retain_until = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_files",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    max_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_files",
                schema: "fleetops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    page = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_files_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "fleetops",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_files_document_id",
                schema: "fleetops",
                table: "document_files",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_number",
                schema: "fleetops",
                table: "documents",
                column: "number");

            migrationBuilder.CreateIndex(
                name: "IX_documents_owner_type_owner_id",
                schema: "fleetops",
                table: "documents",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_status",
                schema: "fleetops",
                table: "documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_pending_files_object_key",
                schema: "fleetops",
                table: "pending_files",
                column: "object_key",
                unique: true);

            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON fleetops.documents, fleetops.document_files, fleetops.pending_files TO fleetops_runtime; REVOKE DELETE, TRUNCATE ON fleetops.documents, fleetops.document_files, fleetops.pending_files FROM fleetops_runtime;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_files",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "pending_files",
                schema: "fleetops");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "fleetops");
        }
    }
}
