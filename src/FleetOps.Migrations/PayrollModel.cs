using FleetOps.Modules.Payroll;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<PayrollExport> PayrollExports => Set<PayrollExport>();

    private static void ConfigurePayroll(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PayrollExport>(entity =>
        {
            entity.ToTable("payroll_exports", table =>
            {
                table.HasCheckConstraint("ck_payroll_kind", "kind IN ('regular', 'supplement', 'correction')");
                table.HasCheckConstraint("ck_payroll_status", "status IN ('generated', 'sent', 'discarded', 'superseded')");
                table.HasCheckConstraint("ck_payroll_period", "period_to >= period_from AND version > 0");
            });
            entity.HasKey(export => export.Id);
            entity.Property(export => export.Id).HasColumnName("id");
            entity.Property(export => export.PeriodFrom).HasColumnName("period_from");
            entity.Property(export => export.PeriodTo).HasColumnName("period_to");
            entity.Property(export => export.Format).HasColumnName("format").HasMaxLength(50);
            entity.Property(export => export.Kind).HasColumnName("kind").HasMaxLength(20);
            entity.Property(export => export.Status).HasColumnName("status").HasMaxLength(20).IsConcurrencyToken();
            entity.Property(export => export.Version).HasColumnName("version");
            entity.Property(export => export.CreatedBy).HasColumnName("created_by");
            entity.Property(export => export.CreatedAt).HasColumnName("created_at");
            entity.Property(export => export.SentBy).HasColumnName("sent_by");
            entity.Property(export => export.SentDate).HasColumnName("sent_date");
            entity.Property(export => export.Note).HasColumnName("note").HasMaxLength(500);
            entity.Property(export => export.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(export => export.CorrectionOf).HasColumnName("correction_of");
            entity.Property(export => export.ShiftVersionsJson).HasColumnName("shift_versions").HasColumnType("jsonb");
            entity.Property(export => export.SnapshotJson).HasColumnName("snapshot").HasColumnType("jsonb");
            entity.Property(export => export.DeltasJson).HasColumnName("deltas").HasColumnType("jsonb");
            entity.Property(export => export.FileKey).HasColumnName("file_key").HasMaxLength(500);
            entity.Property(export => export.Checksum).HasColumnName("checksum").HasMaxLength(64);
            entity.HasIndex(export => new { export.PeriodFrom, export.PeriodTo, export.Version }).IsUnique();
            entity.HasOne<PayrollExport>().WithMany().HasForeignKey(export => export.CorrectionOf).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(export => export.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Shift>().Property(shift => shift.ExportId).HasColumnName("export_id");
        modelBuilder.Entity<Shift>().HasOne<PayrollExport>().WithMany().HasForeignKey(shift => shift.ExportId).OnDelete(DeleteBehavior.Restrict);
    }
}
