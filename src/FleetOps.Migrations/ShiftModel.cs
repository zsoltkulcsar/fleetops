using FleetOps.Modules.Fleet;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed partial class FleetOpsDbContext
{
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftActivity> ShiftActivities => Set<ShiftActivity>();
    public DbSet<Overnight> Overnights => Set<Overnight>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();
    public DbSet<ConflictRecord> ShiftConflicts => Set<ConflictRecord>();

    private static void ConfigureShifts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shift>(entity =>
        {
            entity.ToTable("shifts", table =>
            {
                table.HasCheckConstraint("ck_shifts_dates", "end_at IS NULL OR end_at >= start_at");
                table.HasCheckConstraint("ck_shifts_version", "version > 0 AND last_office_version >= 0 AND last_office_version <= version");
                table.HasCheckConstraint("ck_shifts_running", "(status = 'Running' AND end_at IS NULL) OR (status <> 'Running' AND end_at IS NOT NULL)");
                table.HasCheckConstraint("ck_shifts_status", "status IN ('Running', 'Draft', 'Submitted', 'Approved', 'Rejected', 'Exported')");
                table.HasCheckConstraint("ck_shifts_odometer", "(odometer_start_km IS NULL OR odometer_start_km >= 0) AND (odometer_end_km IS NULL OR odometer_end_km >= COALESCE(odometer_start_km, 0))");
            });
            entity.HasKey(shift => shift.Id);
            entity.Property(shift => shift.Id).HasColumnName("id");
            entity.Property(shift => shift.EmployeeId).HasColumnName("employee_id");
            entity.Property(shift => shift.Date).HasColumnName("date");
            entity.Property(shift => shift.StartAt).HasColumnName("start_at");
            entity.Property(shift => shift.EndAt).HasColumnName("end_at");
            entity.Property(shift => shift.OverrideId).HasColumnName("override_id");
            entity.HasOne<VehicleOverride>().WithMany().HasForeignKey(shift => shift.OverrideId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(shift => shift.TruckId).HasColumnName("truck_id");
            entity.Property(shift => shift.TrailerId).HasColumnName("trailer_id");
            entity.Property(shift => shift.OdometerLower).HasColumnName("odometer_lower");
            entity.Property(shift => shift.OdometerStartKm).HasColumnName("odometer_start_km");
            entity.Property(shift => shift.OdometerEndKm).HasColumnName("odometer_end_km");
            entity.Property(shift => shift.DepartureCheckOk).HasColumnName("departure_check_ok");
            entity.Property(shift => shift.Source).HasColumnName("source").HasConversion<string>();
            entity.Property(shift => shift.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(shift => shift.RejectReason).HasColumnName("reject_reason");
            entity.Property(shift => shift.SubmittedAt).HasColumnName("submitted_at");
            entity.Property(shift => shift.ApprovalNote).HasColumnName("approval_note").HasMaxLength(500);
            entity.Property(shift => shift.ApprovedBy).HasColumnName("approved_by");
            entity.Property(shift => shift.ApprovedAt).HasColumnName("approved_at");
            entity.Property(shift => shift.DeviceTime).HasColumnName("device_time");
            entity.Property(shift => shift.ServerReceivedAt).HasColumnName("server_received_at");
            entity.Property(shift => shift.DeviceTimeGap).HasColumnName("device_time_gap");
            entity.Property(shift => shift.BlockedAtStart).HasColumnName("blocked_at_start");
            entity.Property(shift => shift.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(shift => shift.LastOfficeVersion).HasColumnName("last_office_version");
            entity.HasIndex(shift => shift.EmployeeId).IsUnique().HasFilter("status = 'Running'").HasDatabaseName("ux_shifts_one_running_per_employee");
            entity.HasIndex(shift => new { shift.EmployeeId, shift.Date });
            entity.HasOne<Employee>().WithMany().HasForeignKey(shift => shift.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(shift => shift.TruckId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(shift => shift.TrailerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(shift => shift.Activities).WithOne().HasForeignKey(activity => activity.ShiftId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(shift => shift.Overnights).WithOne().HasForeignKey(overnight => overnight.ShiftId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ShiftActivity>(entity =>
        {
            entity.ToTable("activities", table => table.HasCheckConstraint("ck_activities_dates", "end_at IS NULL OR end_at >= start_at"));
            entity.HasKey(activity => new { activity.ShiftId, activity.Id });
            entity.Property(activity => activity.Id).HasColumnName("id");
            entity.Property(activity => activity.ShiftId).HasColumnName("shift_id");
            entity.Property(activity => activity.Type).HasColumnName("type").HasConversion<string>();
            entity.Property(activity => activity.StartAt).HasColumnName("start_at");
            entity.Property(activity => activity.EndAt).HasColumnName("end_at");
            entity.Property(activity => activity.RecordedAt).HasColumnName("recorded_at");
            entity.HasIndex(activity => activity.ShiftId).IsUnique().HasFilter("end_at IS NULL").HasDatabaseName("ux_activities_one_running_per_shift");
        });
        modelBuilder.Entity<Overnight>(entity =>
        {
            entity.ToTable("overnights");
            entity.HasKey(overnight => overnight.Id);
            entity.Property(overnight => overnight.Id).HasColumnName("id");
            entity.Property(overnight => overnight.ShiftId).HasColumnName("shift_id");
            entity.Property(overnight => overnight.Night).HasColumnName("night");
            entity.Property(overnight => overnight.CountryCode).HasColumnName("country_code").HasMaxLength(2);
            entity.Property(overnight => overnight.Kind).HasColumnName("kind").HasConversion<string>();
            entity.HasIndex(overnight => new { overnight.ShiftId, overnight.Night }).IsUnique();
        });
        modelBuilder.Entity<CommandReceipt>(entity =>
        {
            entity.ToTable("command_receipts");
            entity.HasKey(receipt => new { receipt.EmployeeId, receipt.CommandId });
            entity.Property(receipt => receipt.EmployeeId).HasColumnName("employee_id");
            entity.Property(receipt => receipt.CommandId).HasColumnName("command_id");
            entity.Property(receipt => receipt.AggregateId).HasColumnName("aggregate_id");
            entity.Property(receipt => receipt.Type).HasColumnName("type");
            entity.Property(receipt => receipt.SchemaVersion).HasColumnName("schema_version");
            entity.Property(receipt => receipt.BaseVersion).HasColumnName("base_version");
            entity.Property(receipt => receipt.DeviceTime).HasColumnName("device_time");
            entity.Property(receipt => receipt.ServerReceivedAt).HasColumnName("server_received_at");
            entity.Property(receipt => receipt.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(receipt => receipt.Result).HasColumnName("result").HasColumnType("jsonb");
            entity.Property(receipt => receipt.Outcome).HasColumnName("outcome");
            entity.HasIndex(receipt => new { receipt.EmployeeId, receipt.AggregateId, receipt.ServerReceivedAt });
            entity.HasOne<Employee>().WithMany().HasForeignKey(receipt => receipt.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ConflictRecord>(entity =>
        {
            entity.ToTable("shift_conflicts");
            entity.HasKey(conflict => conflict.Id);
            entity.Property(conflict => conflict.Id).HasColumnName("id");
            entity.Property(conflict => conflict.EmployeeId).HasColumnName("employee_id");
            entity.Property(conflict => conflict.ShiftId).HasColumnName("shift_id");
            entity.Property(conflict => conflict.CommandId).HasColumnName("command_id");
            entity.Property(conflict => conflict.BaseVersion).HasColumnName("base_version");
            entity.Property(conflict => conflict.OfficeVersion).HasColumnName("office_version");
            entity.Property(conflict => conflict.DriverPayload).HasColumnName("driver_payload").HasColumnType("jsonb");
            entity.Property(conflict => conflict.OfficeSnapshot).HasColumnName("office_snapshot").HasColumnType("jsonb");
            entity.Property(conflict => conflict.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(conflict => new { conflict.EmployeeId, conflict.CommandId }).IsUnique();
            entity.HasOne<Shift>().WithMany().HasForeignKey(conflict => conflict.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
