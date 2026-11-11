using FleetOps.Modules.Fleet;
using FleetOps.Modules.Maintenance;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<Maintenance> Maintenance => Set<Maintenance>();
    private static void ConfigureMaintenance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Maintenance>(entity =>
        {
            entity.ToTable("maintenance", table =>
            {
                table.HasCheckConstraint("ck_maintenance_type", "type IN ('service', 'repair', 'inspection_hu', 'inspection_sp', 'tacho_check', 'tyres', 'other')");
                table.HasCheckConstraint("ck_maintenance_due_km", "due_km IS NULL OR due_km >= 0");
                table.HasCheckConstraint("ck_maintenance_cost", "cost_eur IS NULL OR cost_eur >= 0");
                table.HasCheckConstraint("ck_maintenance_odometer", "odometer_at_done_km IS NULL OR odometer_at_done_km >= 0");
            });
            entity.Property(repair => repair.DueDate).HasColumnName("due_date");
            entity.Property(repair => repair.DueKm).HasColumnName("due_km");
            entity.Property(repair => repair.CostEur).HasColumnName("cost_eur").HasPrecision(14, 2);
            entity.Property(repair => repair.InvoiceDocumentId).HasColumnName("invoice_document_id");
            entity.Property(repair => repair.ResultDocumentId).HasColumnName("result_document_id");
            entity.HasOne<FleetOps.Modules.Documents.Document>().WithMany().HasForeignKey(repair => repair.InvoiceDocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FleetOps.Modules.Documents.Document>().WithMany().HasForeignKey(repair => repair.ResultDocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasKey(repair => repair.Id);
            entity.Property(repair => repair.Id).HasColumnName("id");
            entity.Property(repair => repair.Type).HasColumnName("type");
            entity.Property(repair => repair.VehicleId).HasColumnName("vehicle_id");
            entity.Property(repair => repair.Workshop).HasColumnName("workshop").HasMaxLength(500);
            entity.Property(repair => repair.ScheduledFor).HasColumnName("scheduled_for");
            entity.Property(repair => repair.DefectReportIds).HasColumnName("defect_report_ids").HasColumnType("uuid[]");
            entity.Property(repair => repair.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(repair => repair.CancelReason).HasColumnName("cancel_reason").HasMaxLength(2000);
            entity.Property(repair => repair.DoneAt).HasColumnName("done_at");
            entity.Property(repair => repair.OdometerAtDoneKm).HasColumnName("odometer_at_done_km");
            entity.HasIndex(repair => new { repair.VehicleId, repair.ScheduledFor });
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(repair => repair.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Assignment>().HasOne<Maintenance>().WithMany()
            .HasForeignKey(assignment => assignment.MaintenanceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DefectReport>().HasOne<Maintenance>().WithMany()
            .HasForeignKey(defect => defect.RepairId).OnDelete(DeleteBehavior.Restrict);
    }
}
