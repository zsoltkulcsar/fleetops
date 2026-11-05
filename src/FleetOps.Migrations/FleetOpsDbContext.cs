using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class FleetOpsDbContext(DbContextOptions<FleetOpsDbContext> options) : DbContext(options)
{
    public DbSet<MigrationMarker> MigrationMarkers => Set<MigrationMarker>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("fleetops");
        modelBuilder.Entity<MigrationMarker>(entity =>
        {
            entity.ToTable("migration_marker");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        });
    }
}
public sealed class MigrationMarker { public int Id { get; set; } public string Name { get; set; } = "solution-skeleton"; }
