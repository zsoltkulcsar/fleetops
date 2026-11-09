using System.Text.Json;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<Cycle> Cycles => Set<Cycle>();

    private static void ConfigureSchedule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>().Property(employee => employee.CyclePattern)
            .HasColumnName("cycle_pattern").HasColumnType("jsonb")
            .HasConversion(pattern => JsonSerializer.Serialize(pattern, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<CyclePattern>(json, (JsonSerializerOptions?)null));
        modelBuilder.Entity<Employee>().Property(employee => employee.CyclePatternVersion)
            .HasColumnName("cycle_pattern_version").HasDefaultValue(0).IsConcurrencyToken();
        modelBuilder.Entity<Cycle>(entity =>
        {
            entity.ToTable("cycles", table =>
            {
                table.HasCheckConstraint("ck_cycles_dates", "\"from\" <= \"to\"");
                table.HasCheckConstraint("ck_cycles_version", "version > 0");
                table.HasCheckConstraint("ck_cycles_pattern_slot", "pattern_slot IS NULL OR pattern_slot >= 0");
                table.HasCheckConstraint("ck_cycles_source", "source IN ('Generated', 'Manual') AND (source <> 'Generated' OR pattern_slot IS NOT NULL)");
                table.HasCheckConstraint("ck_cycles_status", "status IN ('Planned', 'Active', 'Overrun', 'Done', 'Cancelled')");
            });
            entity.HasKey(cycle => cycle.Id);
            entity.Property(cycle => cycle.Id).HasColumnName("id");
            entity.Property(cycle => cycle.EmployeeId).HasColumnName("employee_id");
            entity.Property(cycle => cycle.VehicleId).HasColumnName("vehicle_id");
            entity.Property(cycle => cycle.PatternSlot).HasColumnName("pattern_slot");
            entity.Property(cycle => cycle.From).HasColumnName("from");
            entity.Property(cycle => cycle.To).HasColumnName("to");
            entity.Property(cycle => cycle.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(cycle => cycle.Source).HasColumnName("source").HasConversion<string>();
            entity.Property(cycle => cycle.FinishedAt).HasColumnName("finished_at");
            entity.Property(cycle => cycle.Version).HasColumnName("version").IsConcurrencyToken();
            entity.HasIndex(cycle => new { cycle.EmployeeId, cycle.PatternSlot }).IsUnique();
            entity.HasOne<Employee>().WithMany().HasForeignKey(cycle => cycle.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FleetOps.Modules.Fleet.Vehicle>().WithMany().HasForeignKey(cycle => cycle.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
