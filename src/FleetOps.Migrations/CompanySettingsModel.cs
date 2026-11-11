using FleetOps.Modules.Setup;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed partial class FleetOpsDbContext
{
    public DbSet<CompanyHoliday> CompanyHolidays => Set<CompanyHoliday>();
    public DbSet<SettingList> SettingLists => Set<SettingList>();
    private static void ConfigureCompanySettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyHoliday>(entity =>
        {
            entity.ToTable("company_holidays");
            entity.HasKey(holiday => holiday.Date);
            entity.Property(holiday => holiday.Date).HasColumnName("date");
            entity.Property(holiday => holiday.Name).HasColumnName("name").HasMaxLength(200);
        });
        modelBuilder.Entity<SettingList>(entity =>
        {
            entity.ToTable("setting_lists");
            entity.HasKey(setting => setting.Key);
            entity.Property(setting => setting.Key).HasColumnName("key");
            entity.Property(setting => setting.Values).HasColumnName("values");
        });
    }
}
