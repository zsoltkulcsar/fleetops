using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext(DbContextOptions<FleetOpsDbContext> options) : IdentityDbContext<FleetOps.Identity.OfficeUser, IdentityRole<Guid>, Guid>(options), IAuditDbContext
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
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("fleetops");
        modelBuilder.Ignore<IdentityPasskeyData>();
        modelBuilder.Entity<FleetOps.Identity.OfficeUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserName).HasColumnName("user_name");
            entity.Property(x => x.NormalizedUserName).HasColumnName("normalized_user_name");
            entity.Property(x => x.Email).HasColumnName("email");
            entity.Property(x => x.NormalizedEmail).HasColumnName("normalized_email");
            entity.Property(x => x.EmailConfirmed).HasColumnName("email_confirmed");
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash");
            entity.Property(x => x.SecurityStamp).HasColumnName("security_stamp");
            entity.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            entity.Property(x => x.PhoneNumber).HasColumnName("phone_number");
            entity.Property(x => x.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
            entity.Property(x => x.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            entity.Property(x => x.LockoutEnd).HasColumnName("lockout_end");
            entity.Property(x => x.LockoutEnabled).HasColumnName("lockout_enabled");
            entity.Property(x => x.AccessFailedCount).HasColumnName("access_failed_count");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.LastAcceptedTotpStep).HasColumnName("last_accepted_totp_step");
        });
        modelBuilder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("roles");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.NormalizedName).HasColumnName("normalized_name");
            entity.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        });
        modelBuilder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("user_claims");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ClaimType).HasColumnName("claim_type");
            entity.Property(x => x.ClaimValue).HasColumnName("claim_value");
        });
        modelBuilder.Entity<IdentityUserRole<Guid>>(entity =>
        {
            entity.ToTable("user_roles");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.RoleId).HasColumnName("role_id");
        });
        modelBuilder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("user_logins");
            entity.Property(x => x.LoginProvider).HasColumnName("login_provider");
            entity.Property(x => x.ProviderKey).HasColumnName("provider_key");
            entity.Property(x => x.ProviderDisplayName).HasColumnName("provider_display_name");
            entity.Property(x => x.UserId).HasColumnName("user_id");
        });
        modelBuilder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("role_claims");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RoleId).HasColumnName("role_id");
            entity.Property(x => x.ClaimType).HasColumnName("claim_type");
            entity.Property(x => x.ClaimValue).HasColumnName("claim_value");
        });
        modelBuilder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("user_tokens");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.LoginProvider).HasColumnName("login_provider");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.Value).HasColumnName("value");
        });
        modelBuilder.Entity<FleetOps.People.Employee>(entity =>
        {
            entity.ToTable("employees"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired(); entity.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            entity.Property(x => x.Roles).HasColumnName("roles").IsRequired(); entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(x => x.StartDate).HasColumnName("start_date"); entity.Property(x => x.EndDate).HasColumnName("end_date");
        });
        modelBuilder.Entity<FleetOps.Identity.OfficeSession>(entity => { entity.ToTable("sessions"); entity.HasKey(x => x.Id); entity.HasIndex(x => x.TokenHash).IsUnique(); entity.Property(x => x.UserId).HasColumnName("user_id"); entity.Property(x => x.EmployeeId).HasColumnName("employee_id"); entity.Property(x => x.TokenHash).HasColumnName("token_hash"); entity.Property(x => x.Device).HasColumnName("device"); entity.Property(x => x.Stage).HasColumnName("stage").HasConversion<string>(); entity.Property(x => x.CreatedAt).HasColumnName("created_at"); entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at"); entity.Property(x => x.RevokedAt).HasColumnName("revoked_at"); entity.Property(x => x.StepUpAt).HasColumnName("step_up_at"); });
        modelBuilder.Entity<FleetOps.Identity.PrivacyAcknowledgement>(entity => { entity.ToTable("privacy_acknowledgements"); entity.HasKey(x => x.Id); entity.Property(x => x.EmployeeId).HasColumnName("employee_id"); entity.Property(x => x.Version).HasColumnName("version"); entity.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at"); });
        modelBuilder.Entity<FleetOps.Identity.PasswordResetToken>(entity => { entity.ToTable("password_reset_tokens"); entity.HasKey(x => x.Id); entity.HasIndex(x => x.TokenHash).IsUnique(); entity.Property(x => x.UserId).HasColumnName("user_id"); entity.Property(x => x.TokenHash).HasColumnName("token_hash"); entity.Property(x => x.ExpiresAt).HasColumnName("expires_at"); entity.Property(x => x.UsedAt).HasColumnName("used_at"); });
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
