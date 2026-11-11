using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfScheduleRequestStore(IConfiguration configuration, IChangedSubjects changed, INotifications notifications) : IScheduleRequestStore
{
    private FleetOpsDbContext Open(CommandContext? context, Guid actorId = default, string reason = "")
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        if (context is null) MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        else MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId, AuditReason = reason };
        if (context is not null) db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public Task LockAsync(CommandContext context, CancellationToken cancellationToken) => EfCycleStore.LockScheduleAsync(context, cancellationToken);
    public async Task<ScheduleChangeRequest?> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.ScheduleChangeRequests.AsNoTracking().SingleOrDefaultAsync(request => request.Id == id, cancellationToken);
    }
    public async Task<Cycle?> CycleAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Cycles.AsNoTracking().SingleOrDefaultAsync(cycle => cycle.Id == id, cancellationToken);
    }
    public async Task<IReadOnlyList<ScheduleRequestView>> ListAsync(Guid? employeeId, CancellationToken cancellationToken)
    {
        await using var db = Open(null);
        var rows = await db.ScheduleChangeRequests.AsNoTracking().Where(request => employeeId == null || request.EmployeeId == employeeId).OrderByDescending(request => request.DeviceTime).ThenBy(request => request.Id).ToArrayAsync(cancellationToken);
        return rows.Select(request => new ScheduleRequestView(request.Id, request.EmployeeId, request.CycleId, request.ProposedFrom, request.ProposedTo, request.Note, request.Status, request.DeclineReason, request.DecidedBy, request.DecidedAt)).ToArray();
    }
    public async Task SaveAsync(ScheduleChangeRequest request, AccessContext actor, string reason, bool replace, bool notify, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context, actor.EmployeeId, reason);
        if (replace)
        {
            var open = await db.ScheduleChangeRequests.Where(row => row.CycleId == request.CycleId && row.Status == ScheduleRequestStatus.Open).ToArrayAsync(cancellationToken);
            foreach (var previous in open)
            {
                previous.Status = ScheduleRequestStatus.Superseded;
                changed.Add("schedule_request", previous.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        var original = await db.ScheduleChangeRequests.SingleOrDefaultAsync(row => row.Id == request.Id, cancellationToken);
        if (original is null) db.ScheduleChangeRequests.Add(request);
        else db.Entry(original).CurrentValues.SetValues(request);
        await db.SaveChangesAsync(cancellationToken);
        changed.Add("schedule_request", request.Id);
        if (notify) await notifications.AddAsync(request.EmployeeId, "schedule_request_decided", new { requestId = request.Id, status = request.Status.ToString().ToLowerInvariant() }, null, context, cancellationToken);
    }
}
