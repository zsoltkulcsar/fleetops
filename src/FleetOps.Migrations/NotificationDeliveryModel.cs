using FleetOps.Modules.Notifications;
using FleetOps.Identity;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed partial class FleetOpsDbContext
{
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<AlertCover> AlertCovers => Set<AlertCover>();
    public DbSet<SmsDailyUsage> SmsDailyUsage => Set<SmsDailyUsage>();
    private static void ConfigureNotificationDelivery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OfficeUser>(entity =>
        {
            entity.Property(user => user.CriticalPush).HasColumnName("critical_push").HasDefaultValue(true);
            entity.Property(user => user.CriticalEmail).HasColumnName("critical_email").HasDefaultValue(true);
            entity.Property(user => user.DigestEmail).HasColumnName("digest_email").HasDefaultValue(true);
        });
        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions");
            entity.HasKey(subscription => subscription.Id);
            entity.Property(subscription => subscription.Id).HasColumnName("id");
            entity.Property(subscription => subscription.EmployeeId).HasColumnName("employee_id");
            entity.Property(subscription => subscription.Endpoint).HasColumnName("endpoint").HasMaxLength(2048);
            entity.Property(subscription => subscription.P256dh).HasColumnName("p256dh");
            entity.Property(subscription => subscription.Auth).HasColumnName("auth");
            entity.HasIndex(subscription => new { subscription.EmployeeId, subscription.Endpoint }).IsUnique();
            entity.HasOne<Employee>().WithMany().HasForeignKey(subscription => subscription.EmployeeId);
        });
        modelBuilder.Entity<AlertCover>(entity =>
        {
            entity.ToTable("alert_covers", table => table.HasCheckConstraint("ck_alert_cover_dates", "to_date >= from_date and from_id <> to_id"));
            entity.HasKey(cover => cover.Id);
            entity.Property(cover => cover.Id).HasColumnName("id");
            entity.Property(cover => cover.FromId).HasColumnName("from_id");
            entity.Property(cover => cover.ToId).HasColumnName("to_id");
            entity.Property(cover => cover.From).HasColumnName("from_date");
            entity.Property(cover => cover.To).HasColumnName("to_date");
            entity.HasIndex(cover => new { cover.FromId, cover.From, cover.To });
            entity.HasIndex(cover => new { cover.ToId, cover.From, cover.To });
            entity.HasOne<Employee>().WithMany().HasForeignKey(cover => cover.FromId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(cover => cover.ToId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SmsDailyUsage>(entity =>
        {
            entity.ToTable("sms_daily_usage");
            entity.HasKey(usage => usage.Date);
            entity.Property(usage => usage.Date).HasColumnName("date");
            entity.Property(usage => usage.Count).HasColumnName("count");
        });
        modelBuilder.Entity<Notification>().Property(notification => notification.DigestDate).HasColumnName("digest_date");
        modelBuilder.Entity<Notification>().HasIndex(notification => new { notification.EmployeeId, notification.TemplateCode, notification.Channel, notification.DigestDate }).IsUnique().HasFilter("digest_date is not null");
        modelBuilder.Entity<Notification>().HasIndex(notification => new { notification.EmployeeId, notification.AlertId, notification.TemplateCode, notification.Channel })
            .IsUnique().HasFilter("alert_id is not null");
    }
}
