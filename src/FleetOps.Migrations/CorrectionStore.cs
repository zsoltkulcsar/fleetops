using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Shifts;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Absences;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfCorrectionStore(IConfiguration configuration, IChangedSubjects changed, INotifications notifications, TimeProvider clock) : ICorrectionStore
{
    private FleetOpsDbContext Open(CommandContext? context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        if (context is null) MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        else MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        if (context is not null) db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public async Task<CorrectionRequest?> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.CorrectionRequests.AsNoTracking().SingleOrDefaultAsync(request => request.Id == id, cancellationToken);
    }
    public async Task<bool> HasOpenAsync(Guid employeeId, Guid? shiftId, DateOnly date, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.CorrectionRequests.AnyAsync(request => request.EmployeeId == employeeId && request.ShiftId == shiftId && request.Date == date && request.Status == "open", cancellationToken);
    }
    public async Task<bool> MissingWorkdayAsync(Guid employeeId, DateOnly date, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        if (await db.Shifts.AnyAsync(shift => shift.EmployeeId == employeeId && shift.Date == date, cancellationToken) ||
            await db.Absences.AnyAsync(absence => absence.EmployeeId == employeeId && absence.Status == AbsenceStatus.Approved && absence.From <= date && absence.To >= date, cancellationToken)) return false;
        return (await new EfIsInCycle(db).ForEmployeeDatesAsync(employeeId, [date], cancellationToken)).Contains(date);
    }
    public async Task<IReadOnlyList<CorrectionView>> ListAsync(Guid? employeeId, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var requests = await db.CorrectionRequests.AsNoTracking().Where(request => employeeId == null || request.EmployeeId == employeeId)
            .OrderByDescending(request => request.RequestedAt).ToArrayAsync(cancellationToken);
        return requests.Select(request => new CorrectionView(request.Id, request.EmployeeId, request.ShiftId, request.Date, request.Reason,
            request.ProposedStartAt, request.ProposedEndAt, request.Note, request.Status, request.RequiresHr, request.DeclineReason,
            JsonSerializer.Deserialize<CorrectionFieldDiff[]>(request.Diffs) ?? [], request.RequestedAt, request.DecidedAt)).ToArray();
    }
    public async Task SaveAsync(CorrectionRequest request, AccessContext actor, bool decided, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        if (decided) db.CorrectionRequests.Update(request);
        else db.CorrectionRequests.Add(request);
        db.AuditEntries.Add(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = "correction_request", EntityId = request.Id,
            FieldName = "status", OldValue = decided ? "open" : null, NewValue = request.Status,
            ActorId = actor.EmployeeId.ToString(), Reason = request.DeclineReason ?? (decided ? "correction_done" : "correction_requested"), CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        changed.Add("correction_request", request.Id);
        if (decided) await notifications.AddAsync(request.EmployeeId, request.Status == "done" ? "correction_done" : "correction_declined",
            new { requestId = request.Id, shiftId = request.ShiftId }, null, context, cancellationToken);
    }
}
