using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    private static void ConfigurePeople(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(employee => employee.DepotId).HasColumnName("depot_id");
            entity.Property(employee => employee.EmploymentType).HasColumnName("employment_type").HasMaxLength(20).HasDefaultValue("full_time");
            entity.Property(employee => employee.LanguagePref).HasColumnName("language_pref").HasMaxLength(2).HasDefaultValue("de");
            entity.Property(employee => employee.AdrRequired).HasColumnName("adr_required").HasDefaultValue(false);
            entity.Property(employee => employee.VacationEntitlementDays).HasColumnName("vacation_entitlement_days").HasPrecision(6, 2);
            entity.HasOne<FleetOps.Modules.Setup.Depot>().WithMany().HasForeignKey(employee => employee.DepotId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
