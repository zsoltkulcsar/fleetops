using FleetOps.Modules.Absences;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<Absence> Absences => Set<Absence>();
    private static void ConfigureAbsences(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Absence>(entity =>
        {
            entity.ToTable("absences", table =>
            {
                table.HasCheckConstraint("ck_absences_dates", "\"from\" <= \"to\" AND (half_day IS NULL OR \"from\" = \"to\")");
            });
            entity.HasKey(absence => absence.Id);
            entity.Property(absence => absence.Id).HasColumnName("id");
            entity.Property(absence => absence.EmployeeId).HasColumnName("employee_id");
            entity.Property(absence => absence.From).HasColumnName("from");
            entity.Property(absence => absence.To).HasColumnName("to");
            entity.Property(absence => absence.HalfDay).HasColumnName("half_day").HasConversion<string>();
            entity.Property(absence => absence.Type).HasColumnName("type").HasConversion<string>();
            entity.Property(absence => absence.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(absence => absence.DecidedBy).HasColumnName("decided_by");
            entity.HasIndex(absence => new { absence.EmployeeId, absence.From, absence.To });
            entity.HasOne<Employee>().WithMany().HasForeignKey(absence => absence.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(absence => absence.DecidedBy).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
