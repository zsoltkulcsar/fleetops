using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed partial class FleetOpsDbContext
{
    public DbSet<CorrectionRequest> CorrectionRequests => Set<CorrectionRequest>();
    private static void ConfigureCorrections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CorrectionRequest>(entity =>
        {
            entity.ToTable("correction_requests");
            entity.HasKey(request => request.Id);
            entity.HasIndex(request => new { request.EmployeeId, request.CommandId }).IsUnique();
            entity.HasIndex(request => new { request.EmployeeId, request.Date });
            entity.Property(request => request.Reason).HasMaxLength(40);
            entity.Property(request => request.Note).HasMaxLength(1000);
            entity.Property(request => request.DeclineReason).HasMaxLength(1000);
            entity.Property(request => request.Status).HasMaxLength(16);
            entity.Property(request => request.Before).HasColumnType("jsonb");
            entity.Property(request => request.Diffs).HasColumnType("jsonb");
            entity.HasOne<Shift>().WithMany().HasForeignKey(request => request.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
