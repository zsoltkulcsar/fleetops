using FleetOps.Modules.Fleet;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed partial class FleetOpsDbContext
{
    public DbSet<VehicleOverride> VehicleOverrides => Set<VehicleOverride>();
    private static void ConfigureVehicleOverrides(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VehicleOverride>(entity =>
        {
            entity.ToTable("vehicle_overrides");
            entity.HasKey(release => release.Id);
            entity.Property(release => release.Id).HasColumnName("id");
            entity.Property(release => release.VehicleId).HasColumnName("vehicle_id");
            entity.Property(release => release.By).HasColumnName("by");
            entity.Property(release => release.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(release => release.At).HasColumnName("at");
            entity.Property(release => release.ValidUntil).HasColumnName("valid_until");
            entity.Property(release => release.DocTypes).HasColumnName("doc_types");
            entity.HasIndex(release => new { release.VehicleId, release.ValidUntil });
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(release => release.VehicleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(release => release.By).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
