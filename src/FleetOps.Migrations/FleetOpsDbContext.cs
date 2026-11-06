using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using FleetOps.Identity;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext(DbContextOptions<FleetOpsDbContext> options) : IdentityDbContext<FleetOps.Identity.OfficeUser, IdentityRole<Guid>, Guid>(options), IAuditDbContext
{
    public DbSet<MigrationMarker> MigrationMarkers => Set<MigrationMarker>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<PlateHistory> PlateHistories => Set<PlateHistory>();
    public DbSet<Depot> Depots => Set<Depot>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentFile> DocumentFiles => Set<DocumentFile>();
    public DbSet<PendingFile> PendingFiles => Set<PendingFile>();
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
            entity.Property(x => x.FailedSecondFactorAttempts).HasColumnName("failed_second_factor_attempts");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.LastAcceptedTotpStep).HasColumnName("last_accepted_totp_step");
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
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
        modelBuilder.Entity<FleetOps.Identity.OfficeSession>(entity => { entity.ToTable("sessions"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.HasIndex(x => x.TokenHash).IsUnique(); entity.HasIndex(x => x.UserId); entity.HasOne<FleetOps.Identity.OfficeUser>().WithMany().HasForeignKey(x => x.UserId); entity.HasOne<FleetOps.People.Employee>().WithMany().HasForeignKey(x => x.EmployeeId); entity.Property(x => x.UserId).HasColumnName("user_id"); entity.Property(x => x.EmployeeId).HasColumnName("employee_id"); entity.Property(x => x.TokenHash).HasColumnName("token_hash"); entity.Property(x => x.Device).HasColumnName("device"); entity.Property(x => x.Stage).HasColumnName("stage").HasConversion(v => v.ToString().ToSnakeCase(), v => Enum.Parse<SessionStage>(v.Replace("_", ""), true)); entity.Property(x => x.CreatedAt).HasColumnName("created_at"); entity.Property(x => x.StageChangedAt).HasColumnName("stage_changed_at"); entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at"); entity.Property(x => x.RevokedAt).HasColumnName("revoked_at"); entity.Property(x => x.StepUpAt).HasColumnName("step_up_at"); entity.Property(x => x.FailedSecondFactorAttempts).HasColumnName("failed_second_factor_attempts"); });
        modelBuilder.Entity<FleetOps.Identity.PrivacyAcknowledgement>(entity => { entity.ToTable("privacy_acknowledgements"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.HasIndex(x => x.EmployeeId); entity.HasOne<FleetOps.People.Employee>().WithMany().HasForeignKey(x => x.EmployeeId); entity.Property(x => x.EmployeeId).HasColumnName("employee_id"); entity.Property(x => x.Version).HasColumnName("version"); entity.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at"); });
        modelBuilder.Entity<FleetOps.Identity.PasswordResetToken>(entity => { entity.ToTable("password_reset_tokens"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.HasIndex(x => x.TokenHash).IsUnique(); entity.HasIndex(x => x.UserId); entity.HasOne<FleetOps.Identity.OfficeUser>().WithMany().HasForeignKey(x => x.UserId); entity.Property(x => x.UserId).HasColumnName("user_id"); entity.Property(x => x.TokenHash).HasColumnName("token_hash"); entity.Property(x => x.ExpiresAt).HasColumnName("expires_at"); entity.Property(x => x.UsedAt).HasColumnName("used_at"); });
        modelBuilder.Entity<Vehicle>(entity => { entity.ToTable("vehicles"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>(); entity.Property(x => x.Plate).HasColumnName("plate").HasMaxLength(12).IsRequired(); entity.Property(x => x.Vin).HasColumnName("vin").HasMaxLength(17).IsRequired(); entity.Property(x => x.Make).HasColumnName("make").IsRequired(); entity.Property(x => x.Model).HasColumnName("model").IsRequired(); entity.Property(x => x.Year).HasColumnName("year"); entity.Property(x => x.GrossWeightKg).HasColumnName("gross_weight_kg"); entity.Property(x => x.RequiredLicenceClass).HasColumnName("required_licence_class"); entity.Property(x => x.Ownership).HasColumnName("ownership").HasConversion<string>(); entity.Property(x => x.LeaseEndDate).HasColumnName("lease_end_date"); entity.Property(x => x.TrailerType).HasColumnName("trailer_type").HasConversion<string>(); entity.Property(x => x.OdometerKm).HasColumnName("odometer_km"); entity.Property(x => x.OdometerUpdatedAt).HasColumnName("odometer_updated_at"); entity.Property(x => x.DepotId).HasColumnName("depot_id"); entity.Property(x => x.ServiceIntervalKm).HasColumnName("service_interval_km"); entity.Property(x => x.ServiceIntervalMonths).HasColumnName("service_interval_months"); entity.Property(x => x.LastTachoDownloadAt).HasColumnName("last_tacho_download_at"); entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>(); entity.Property(x => x.RetiredReason).HasColumnName("retired_reason").HasConversion<string>(); entity.Property(x => x.RetiredAt).HasColumnName("retired_at"); entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); entity.Property(x => x.CreatedAt).HasColumnName("created_at"); entity.Property(x => x.UpdatedAt).HasColumnName("updated_at"); entity.HasIndex(x => x.Plate).IsUnique().HasFilter("status = 'Active'").HasDatabaseName("IX_vehicles_plate"); entity.HasIndex(x => new { x.Plate, x.Id }).HasDatabaseName("IX_vehicles_list_plate_id"); entity.HasIndex(x => x.Vin).IsUnique().HasFilter("status = 'Active'"); });
        modelBuilder.Entity<PlateHistory>(entity => { entity.ToTable("vehicle_plate_history"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.VehicleId).HasColumnName("vehicle_id"); entity.Property(x => x.Plate).HasColumnName("plate").IsRequired(); entity.Property(x => x.ChangedAt).HasColumnName("changed_at"); });
        modelBuilder.Entity<Depot>(entity => { entity.ToTable("depots"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.Name).HasColumnName("name").IsRequired(); entity.Property(x => x.Address).HasColumnName("address").IsRequired(); });
        modelBuilder.Entity<Depot>().HasData(new Depot { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Default depot", Address = "Default address" });
        modelBuilder.Entity<AuditEntry>(entity => { entity.ToTable("audit_entries"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.EntityType).HasColumnName("entity_type").IsRequired(); entity.Property(x => x.EntityId).HasColumnName("entity_id"); entity.Property(x => x.FieldName).HasColumnName("field_name").IsRequired(); entity.Property(x => x.OldValue).HasColumnName("old_value"); entity.Property(x => x.NewValue).HasColumnName("new_value"); entity.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired(); entity.Property(x => x.Reason).HasColumnName("reason").IsRequired(); entity.Property(x => x.CreatedAt).HasColumnName("created_at"); });
        modelBuilder.Entity<Document>(entity => { entity.ToTable("documents"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.OwnerType).HasColumnName("owner_type").HasConversion<string>(); entity.Property(x => x.OwnerId).HasColumnName("owner_id"); entity.Property(x => x.Type).HasColumnName("type").HasConversion(v => v == DocumentType.LicenceC_CE ? "licence_c_ce" : v.ToString().ToSnakeCase(), v => v == "licence_c_ce" ? DocumentType.LicenceC_CE : Enum.Parse<DocumentType>(v.Replace("_", ""), true)); entity.Property(x => x.Number).HasColumnName("number"); entity.Property(x => x.IssuedAt).HasColumnName("issued_at"); entity.Property(x => x.ExpiresAt).HasColumnName("expires_at"); entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>(); entity.Property(x => x.VerifiedBy).HasColumnName("verified_by"); entity.Property(x => x.VerifiedAt).HasColumnName("verified_at"); entity.Property(x => x.RejectReason).HasColumnName("reject_reason"); entity.Property(x => x.ReplacesId).HasColumnName("replaces_id"); entity.Property(x => x.Categories).HasColumnName("categories"); entity.Property(x => x.LastSightCheckAt).HasColumnName("last_sight_check_at"); entity.Property(x => x.IssuedReason).HasColumnName("issued_reason").HasConversion<string>(); entity.Property(x => x.ImportAttested).HasColumnName("import_attested"); entity.Property(x => x.RetainUntil).HasColumnName("retain_until"); entity.Property(x => x.CreatedBy).HasColumnName("created_by"); entity.Property(x => x.CreatedAt).HasColumnName("created_at"); entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); entity.HasIndex(x => new { x.OwnerType, x.OwnerId }); entity.HasIndex(x => x.Status); entity.HasIndex(x => x.Number); });
        modelBuilder.Entity<DocumentFile>(entity => { entity.ToTable("document_files"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.DocumentId).HasColumnName("document_id"); entity.Property(x => x.ObjectKey).HasColumnName("object_key"); entity.Property(x => x.ContentType).HasColumnName("content_type"); entity.Property(x => x.SizeBytes).HasColumnName("size_bytes"); entity.Property(x => x.Page).HasColumnName("page").HasConversion<string>(); entity.Property(x => x.SortOrder).HasColumnName("sort_order"); entity.HasIndex(x => x.DocumentId); });
        modelBuilder.Entity<PendingFile>(entity => { entity.ToTable("pending_files"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.ObjectKey).HasColumnName("object_key"); entity.Property(x => x.ActorId).HasColumnName("actor_id"); entity.Property(x => x.Purpose).HasColumnName("purpose"); entity.Property(x => x.ContentType).HasColumnName("content_type"); entity.Property(x => x.MaxSizeBytes).HasColumnName("max_size_bytes"); entity.Property(x => x.CreatedAt).HasColumnName("created_at"); entity.Property(x => x.ExpiresAt).HasColumnName("expires_at"); entity.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at"); entity.HasIndex(x => x.ObjectKey).IsUnique(); });
        modelBuilder.Entity<MigrationMarker>(entity =>
        {
            entity.ToTable("migration_marker");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        });
        modelBuilder.Entity<Vehicle>().Property(x => x.ManualOffRoad).HasColumnName("manual_off_road").HasDefaultValue(false);
    }
}
public sealed class MigrationMarker { public int Id { get; set; } public string Name { get; set; } = "solution-skeleton"; }
internal static class SnakeCaseExtensions
{
    public static string ToSnakeCase(this string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : c.ToString().ToLowerInvariant()));
}
