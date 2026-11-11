using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetOps.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EpicNineMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_type",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.AddColumn<bool>(
                name: "odometer_lower",
                schema: "fleetops",
                table: "shifts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_eur",
                schema: "fleetops",
                table: "maintenance",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "fleetops",
                table: "maintenance",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "due_km",
                schema: "fleetops",
                table: "maintenance",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invoice_document_id",
                schema: "fleetops",
                table: "maintenance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "result_document_id",
                schema: "fleetops",
                table: "maintenance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_tacho_download_at",
                schema: "fleetops",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_invoice_document_id",
                schema: "fleetops",
                table: "maintenance",
                column: "invoice_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_result_document_id",
                schema: "fleetops",
                table: "maintenance",
                column: "result_document_id");

            // Existing repairs (Epic 5) had only a scheduled date: use it as the due date (E9-P1).
            migrationBuilder.Sql("UPDATE fleetops.maintenance SET due_date = scheduled_for WHERE due_date IS NULL AND scheduled_for IS NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_cost",
                schema: "fleetops",
                table: "maintenance",
                sql: "cost_eur IS NULL OR cost_eur >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_due_km",
                schema: "fleetops",
                table: "maintenance",
                sql: "due_km IS NULL OR due_km >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_type",
                schema: "fleetops",
                table: "maintenance",
                sql: "type IN ('service', 'repair', 'inspection_hu', 'inspection_sp', 'tacho_check', 'tyres', 'other')");

            migrationBuilder.AddForeignKey(
                name: "FK_maintenance_documents_invoice_document_id",
                schema: "fleetops",
                table: "maintenance",
                column: "invoice_document_id",
                principalSchema: "fleetops",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_maintenance_documents_result_document_id",
                schema: "fleetops",
                table: "maintenance",
                column: "result_document_id",
                principalSchema: "fleetops",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_maintenance_documents_invoice_document_id",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropForeignKey(
                name: "FK_maintenance_documents_result_document_id",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_invoice_document_id",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_result_document_id",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_cost",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_due_km",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_type",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropColumn(
                name: "odometer_lower",
                schema: "fleetops",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "cost_eur",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropColumn(
                name: "due_km",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropColumn(
                name: "invoice_document_id",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropColumn(
                name: "result_document_id",
                schema: "fleetops",
                table: "maintenance");

            migrationBuilder.DropColumn(
                name: "last_tacho_download_at",
                schema: "fleetops",
                table: "employees");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_type",
                schema: "fleetops",
                table: "maintenance",
                sql: "type = 'repair'");
        }
    }
}
