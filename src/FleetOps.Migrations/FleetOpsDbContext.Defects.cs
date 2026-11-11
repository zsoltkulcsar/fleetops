using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<DefectReport> DefectReports => Set<DefectReport>();
    public DbSet<DefectPhoto> DefectPhotos => Set<DefectPhoto>();

    private static void ConfigureDefects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DefectReport>(entity =>
        {
            entity.ToTable("defect_reports");
            entity.HasKey(defect => defect.Id);
            foreach (var property in entity.Metadata.GetProperties())
                property.SetColumnName(property.Name.ToSnakeCase());
            entity.Property(defect => defect.Category).HasConversion(value => value.ToString().ToSnakeCase(), value => Enum.Parse<DefectCategory>(value.Replace("_", ""), true));
            entity.Property(defect => defect.Severity).HasConversion(value => value.ToString().ToSnakeCase(), value => Enum.Parse<DefectSeverity>(value, true));
            entity.Property(defect => defect.Origin).HasConversion(value => value.ToString().ToSnakeCase(), value => Enum.Parse<DefectOrigin>(value.Replace("_", ""), true));
            entity.Property(defect => defect.Status).HasConversion(value => value.ToString().ToSnakeCase(), value => Enum.Parse<DefectStatus>(value.Replace("_", ""), true)).IsConcurrencyToken();
            entity.Property(defect => defect.Description).HasMaxLength(4000);
            entity.Property(defect => defect.CloseReason).HasMaxLength(4000);
            entity.HasIndex(defect => new { defect.VehicleId, defect.Status });
            entity.HasIndex(defect => defect.CommandId).IsUnique();
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(defect => defect.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DefectPhoto>(entity =>
        {
            entity.ToTable("defect_photos");
            entity.HasKey(photo => photo.Id);
            foreach (var property in entity.Metadata.GetProperties())
                property.SetColumnName(property.Name.ToSnakeCase());
            entity.HasOne<DefectReport>().WithMany().HasForeignKey(photo => photo.DefectId);
            entity.HasIndex(photo => new { photo.DefectId, photo.SortOrder }).IsUnique();
            entity.HasIndex(photo => photo.ObjectKey).IsUnique();
        });
    }
}
