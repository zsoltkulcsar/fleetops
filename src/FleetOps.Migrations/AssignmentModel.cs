using FleetOps.Modules.Fleet;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<Assignment> Assignments => Set<Assignment>();
    private static void ConfigureAssignments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Assignment>(entity =>
        {
            entity.ToTable("assignments", table => table.HasCheckConstraint("ck_assignments_dates", "\"to\" IS NULL OR \"to\" >= \"from\""));
            entity.HasKey(assignment => assignment.Id);
            entity.Property(assignment => assignment.Id).HasColumnName("id");
            entity.Property(assignment => assignment.EmployeeId).HasColumnName("employee_id");
            entity.Property(assignment => assignment.VehicleId).HasColumnName("vehicle_id");
            entity.Property(assignment => assignment.From).HasColumnName("from");
            entity.Property(assignment => assignment.To).HasColumnName("to");
            entity.Property(assignment => assignment.MaintenanceId).HasColumnName("maintenance_id");
            entity.Property(assignment => assignment.Note).HasColumnName("note");
            entity.HasIndex(assignment => new { assignment.EmployeeId, assignment.From });
            entity.HasOne<Employee>().WithMany().HasForeignKey(assignment => assignment.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(assignment => assignment.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
