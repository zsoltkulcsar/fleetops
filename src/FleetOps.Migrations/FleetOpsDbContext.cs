using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Setup;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class FleetOpsDbContext(DbContextOptions<FleetOpsDbContext> options) : DbContext(options), IAuditDbContext
{
    public DbSet<MigrationMarker> MigrationMarkers => Set<MigrationMarker>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<PlateHistory> PlateHistories => Set<PlateHistory>();
    public DbSet<Depot> Depots => Set<Depot>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public Guid AuditActorId { get; set; }
    public string AuditReason { get; set; } = "";
    public void AddAudit(AuditEntry entry) => AuditEntries.Add(entry);
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("fleetops");
        modelBuilder.Entity<Vehicle>(entity => { entity.ToTable("vehicles"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>(); entity.Property(x => x.Plate).HasColumnName("plate").HasMaxLength(12).IsRequired(); entity.Property(x => x.Vin).HasColumnName("vin").HasMaxLength(17).IsRequired(); entity.Property(x => x.Make).HasColumnName("make").IsRequired(); entity.Property(x => x.Model).HasColumnName("model").IsRequired(); entity.Property(x => x.Ownership).HasColumnName("ownership").HasConversion<string>(); entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>(); entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); entity.HasIndex(x => x.Plate).IsUnique().HasFilter("status = 'Active'"); entity.HasIndex(x => x.Vin).IsUnique().HasFilter("status = 'Active'"); });
        modelBuilder.Entity<PlateHistory>(entity => { entity.ToTable("vehicle_plate_history"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.VehicleId).HasColumnName("vehicle_id"); entity.Property(x => x.Plate).HasColumnName("plate").IsRequired(); });
        modelBuilder.Entity<Depot>(entity => { entity.ToTable("depots"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.Name).HasColumnName("name").IsRequired(); entity.Property(x => x.Address).HasColumnName("address").IsRequired(); });
        modelBuilder.Entity<AuditEntry>(entity => { entity.ToTable("audit_entries"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.EntityType).HasColumnName("entity_type").IsRequired(); entity.Property(x => x.EntityId).HasColumnName("entity_id"); entity.Property(x => x.FieldName).HasColumnName("field_name").IsRequired(); entity.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired(); entity.Property(x => x.Reason).HasColumnName("reason").IsRequired(); entity.Property(x => x.CreatedAt).HasColumnName("created_at"); });
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
