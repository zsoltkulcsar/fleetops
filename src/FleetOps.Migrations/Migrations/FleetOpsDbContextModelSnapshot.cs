using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using FleetOps.Migrations;

#nullable disable
namespace FleetOps.Migrations.Migrations;

[DbContext(typeof(FleetOpsDbContext))]
partial class FleetOpsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("fleetops");
        modelBuilder.Entity("FleetOps.Migrations.MigrationMarker", b => { b.Property<int>("Id"); b.Property<string>("Name").HasColumnName("name").HasMaxLength(100); b.HasKey("Id"); b.ToTable("migration_marker", "fleetops"); });
    }
}
