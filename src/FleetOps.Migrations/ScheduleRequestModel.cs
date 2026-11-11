using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed partial class FleetOpsDbContext
{
    public DbSet<ScheduleChangeRequest> ScheduleChangeRequests => Set<ScheduleChangeRequest>();
    private static void ConfigureScheduleRequests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScheduleChangeRequest>(entity =>
        {
            entity.ToTable("schedule_change_requests", table => table.HasCheckConstraint("ck_schedule_request_dates", "proposed_from <= proposed_to"));
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).HasColumnName("id");
            entity.Property(request => request.EmployeeId).HasColumnName("employee_id");
            entity.Property(request => request.CycleId).HasColumnName("cycle_id");
            entity.Property(request => request.ProposedFrom).HasColumnName("proposed_from");
            entity.Property(request => request.ProposedTo).HasColumnName("proposed_to");
            entity.Property(request => request.Note).HasColumnName("note").HasMaxLength(2000);
            entity.Property(request => request.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(request => request.DeclineReason).HasColumnName("decline_reason").HasMaxLength(2000);
            entity.Property(request => request.DecidedBy).HasColumnName("decided_by");
            entity.Property(request => request.DecidedAt).HasColumnName("decided_at");
            entity.Property(request => request.CommandId).HasColumnName("command_id");
            entity.Property(request => request.DeviceTime).HasColumnName("device_time");
            entity.HasIndex(request => new { request.EmployeeId, request.CommandId }).IsUnique();
            entity.HasIndex(request => request.CycleId).IsUnique().HasFilter("status = 'Open'");
            entity.HasOne<Employee>().WithMany().HasForeignKey(request => request.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(request => request.DecidedBy).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Cycle>().WithMany().HasForeignKey(request => request.CycleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
