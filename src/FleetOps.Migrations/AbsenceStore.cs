using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfAbsenceStore(IConfiguration configuration, IChangedSubjects changed, INotifications notifications) : IAbsenceStore
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
    public async Task LockAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken)
    {
        await new EfShiftCommandStore().LockEmployeeAsync(employeeId, context, cancellationToken);
        await EfCycleStore.LockEmployeeAsync(employeeId, context, cancellationToken);
    }
    public async Task<Absence?> LoadAsync(Guid id, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Absences.AsNoTracking().SingleOrDefaultAsync(absence => absence.Id == id, cancellationToken);
    }
    public async Task<AbsenceFacts?> FactsAsync(Guid employeeId, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken);
        if (employee is null) return null;
        var absences = await db.Absences.AsNoTracking().Where(absence => absence.EmployeeId == employeeId).ToArrayAsync(cancellationToken);
        var holidays = await db.CompanyHolidays.Select(holiday => holiday.Date).ToArrayAsync(cancellationToken);
        return new(employee.Id, employee.FirstName + " " + employee.LastName, employee.VacationEntitlementDays, employee.VacationOpeningBalance, employee.VacationOpeningBalanceYear, absences, holidays.ToHashSet());
    }
    public async Task<IReadOnlyList<Absence>> ListAsync(DateOnly from, DateOnly to, Guid? employeeId, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Absences.AsNoTracking().Where(absence => absence.From <= to && absence.To >= from && (employeeId == null || absence.EmployeeId == employeeId)).OrderBy(absence => absence.From).ThenBy(absence => absence.Id).ToArrayAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<AbsenceShift>> ShiftsAsync(Guid employeeId, DateOnly from, DateOnly to, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var rows = await db.Shifts.AsNoTracking().Where(shift => shift.EmployeeId == employeeId && shift.Date >= from && shift.Date <= to).OrderBy(shift => shift.Date).ToArrayAsync(cancellationToken);
        return rows.Select(shift => new AbsenceShift(shift.Id, shift.Date, JsonNamingPolicy.SnakeCaseLower.ConvertName(shift.Status.ToString()))).ToArray();
    }
    public async Task<IReadOnlyList<string>> OthersAsync(Absence absence, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var ids = db.Absences.Where(other => other.EmployeeId != absence.EmployeeId && other.Status == AbsenceStatus.Approved && other.From <= absence.To && other.To >= absence.From).Select(other => other.EmployeeId);
        return await db.Employees.Where(employee => ids.Contains(employee.Id)).OrderBy(employee => employee.LastName).ThenBy(employee => employee.Id).Select(employee => employee.FirstName + " " + employee.LastName).ToArrayAsync(cancellationToken);
    }
    public async Task SaveAsync(Absence absence, AccessContext actor, string reason, bool notifyDriver, bool notifyHr, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context, actor.EmployeeId, reason);
        var original = await db.Absences.SingleOrDefaultAsync(row => row.Id == absence.Id, cancellationToken);
        if (original is null) db.Absences.Add(absence);
        else db.Entry(original).CurrentValues.SetValues(absence);
        await db.SaveChangesAsync(cancellationToken);
        changed.Add("employee", absence.EmployeeId);
        var cycles = await db.Cycles.Where(cycle => cycle.EmployeeId == absence.EmployeeId && cycle.Status != CycleStatus.Cancelled && cycle.Status != CycleStatus.Done).Select(cycle => cycle.Id).ToArrayAsync(cancellationToken);
        foreach (var cycleId in cycles) changed.Add("cycle", cycleId);
        var shifts = await db.Shifts.Where(shift => shift.EmployeeId == absence.EmployeeId).Select(shift => shift.Id).ToArrayAsync(cancellationToken);
        foreach (var shiftId in shifts) changed.Add("shift", shiftId);
        // Notification parameters contain IDs/codes only. Reasons remain on the protected absence read model.
        if (notifyDriver) await notifications.AddAsync(absence.EmployeeId, "vacation_decided", new { absenceId = absence.Id, status = JsonNamingPolicy.SnakeCaseLower.ConvertName(absence.Status.ToString()) }, null, context, cancellationToken);
        if (notifyHr)
        {
            var recipients = await db.Employees.Where(employee => employee.Status == EmployeeStatus.Active && employee.Roles.Contains("\"hr\"")).Select(employee => employee.Id).ToArrayAsync(cancellationToken);
            foreach (var recipient in recipients) await notifications.AddAsync(recipient, "absence_recorded", new { absenceId = absence.Id }, null, context, cancellationToken);
        }
    }
}
