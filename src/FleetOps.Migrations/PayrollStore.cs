using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Payroll;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfPayrollStore(FleetOpsDbContext readDb, IConfiguration configuration, TimeProvider clock) : IPayrollStore
{
    public async Task LockAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('payroll-exports', 0))", cancellationToken);
        var employeeIds = await db.Employees.OrderBy(employee => employee.Id).Select(employee => employee.Id).ToArrayAsync(cancellationToken);
        foreach (var employeeId in employeeIds)
            await new EfShiftCommandStore().LockEmployeeAsync(employeeId, context, cancellationToken);
    }

    public async Task<PayrollMonthFacts> ReadAsync(DateOnly month, CommandContext? context, CancellationToken cancellationToken)
    {
        if (context is null) return await ReadFromAsync(readDb, month, cancellationToken);
        await using var db = Create(context);
        return await ReadFromAsync(db, month, cancellationToken);
    }

    private async Task<PayrollMonthFacts> ReadFromAsync(FleetOpsDbContext db, DateOnly month, CancellationToken cancellationToken)
    {
        var end = month.AddMonths(1).AddDays(-1);
        var employees = await db.Employees.AsNoTracking().Where(employee => employee.Roles.Contains("\"driver\"") && employee.StartDate <= end &&
            (employee.EndDate == null || employee.EndDate >= month)).OrderBy(employee => employee.Id).ToArrayAsync(cancellationToken);
        var ids = employees.Select(employee => employee.Id).ToArray();
        var shifts = await db.Shifts.AsNoTracking().Include(shift => shift.Activities).Include(shift => shift.Overnights)
            .Where(shift => ids.Contains(shift.EmployeeId) && shift.Date >= month && shift.Date <= end).ToArrayAsync(cancellationToken);
        var absences = await db.Absences.AsNoTracking().Where(absence => ids.Contains(absence.EmployeeId) && absence.Status == AbsenceStatus.Approved && absence.From <= end && absence.To >= month).ToArrayAsync(cancellationToken);
        var holidays = (await db.CompanyHolidays.AsNoTracking().Where(holiday => holiday.Date >= month && holiday.Date <= end.AddDays(2)).Select(holiday => holiday.Date).ToArrayAsync(cancellationToken)).ToHashSet();
        var timezone = CompanyTimeService.Resolve(configuration["Company:Timezone"] ?? throw new InvalidOperationException("company_timezone_required"));
        var people = new List<PayrollPerson>();
        var shiftFacts = new List<PayrollShiftFact>();
        var absenceDays = new List<PayrollDay>();
        foreach (var employee in employees)
        {
            var name = employee.FirstName + " " + employee.LastName;
            var ownShifts = shifts.Where(shift => shift.EmployeeId == employee.Id).Select(shift => new PayrollShiftFact(shift.Id, shift.Version, shift.Status.ToString(), shift.ExportId,
                PayrollFactsBuilder.ShiftDays(employee.Id, name, employee.PersonnelNumber, shift.Date,
                    shift.Activities.Select(activity => new PayrollActivityFact(activity.Type.ToString(), activity.StartAt, activity.EndAt)).ToArray(),
                    shift.Overnights.Select(night => new PayrollNightFact(night.Night, night.CountryCode)).ToArray(), holidays, timezone))).ToArray();
            var ownAbsences = absences.Where(absence => absence.EmployeeId == employee.Id)
                .Select(absence => new PayrollAbsenceFact(absence.From, absence.To, AbsenceCode(absence.Type), absence.HalfDay is not null)).ToArray();
            var dates = Enumerable.Range(0, end.Day).Select(month.AddDays).Where(date => date >= employee.StartDate && (employee.EndDate == null || date <= employee.EndDate)).ToArray();
            var scheduled = await new EfIsInCycle(db).ForEmployeeDatesAsync(employee.Id, dates, cancellationToken);
            people.Add(PayrollFactsBuilder.Person(employee.Id, name, employee.PersonnelNumber, employee.EndDate, month, scheduled, holidays, ownShifts, ownAbsences));
            shiftFacts.AddRange(ownShifts);
            absenceDays.AddRange(PayrollFactsBuilder.AbsenceDays(employee.Id, name, employee.PersonnelNumber, month, employee.StartDate, employee.EndDate, scheduled, holidays, ownAbsences));
        }
        return new(people, shiftFacts, PayrollViews.Combine(absenceDays));
    }

    private static string AbsenceCode(AbsenceType type) => type == AbsenceType.TimeOffInLieu ? "time_off_in_lieu" : type.ToString().ToLowerInvariant();

    public async Task<IReadOnlyList<PayrollExport>> HistoryAsync(DateOnly month, CommandContext? context, CancellationToken cancellationToken)
    {
        if (context is null) return await readDb.PayrollExports.AsNoTracking().Where(export => export.PeriodFrom == month).OrderByDescending(export => export.Version).ToArrayAsync(cancellationToken);
        await using var db = Create(context);
        return await db.PayrollExports.AsNoTracking().Where(export => export.PeriodFrom == month).OrderByDescending(export => export.Version).ToArrayAsync(cancellationToken);
    }

    public async Task<PayrollExport?> FindAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return await db.PayrollExports.AsNoTracking().SingleOrDefaultAsync(export => export.Id == id, cancellationToken);
    }

    public async Task SaveExportAsync(PayrollExport export, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, actorId, reason);
        var existing = await db.PayrollExports.SingleOrDefaultAsync(row => row.Id == export.Id, cancellationToken);
        var previous = existing?.Status;
        if (existing is null) db.PayrollExports.Add(export);
        else db.Entry(existing).CurrentValues.SetValues(export);
        Audit(db, nameof(PayrollExport), export.Id, "Status", previous, export.Status, actorId, reason);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetShiftsAsync(IReadOnlyList<PayrollShiftVersion> shifts, string status, Guid? exportId, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, actorId, reason);
        var ids = shifts.Select(shift => shift.ShiftId).ToArray();
        var rows = await db.Shifts.Where(shift => ids.Contains(shift.Id)).ToArrayAsync(cancellationToken);
        var changed = shifts.Where(expected => rows.SingleOrDefault(row => row.Id == expected.ShiftId)?.Version != expected.Version).ToArray();
        if (changed.Length > 0) throw new PayrollConflictException("export_changed", changed);
        foreach (var row in rows)
        {
            row.Status = Enum.Parse<ShiftStatus>(status);
            row.ExportId = exportId;
            row.Version++;
            row.LastOfficeVersion = row.Version;
            if (row.Status == ShiftStatus.Submitted)
            {
                row.ApprovedAt = null;
                row.ApprovedBy = null;
                row.ApprovalNote = null;
                row.SubmittedAt = clock.GetUtcNow();
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(Guid ExportId, int Version)?> ExportedShiftAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        var shift = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(shift => shift.Id == shiftId && shift.Status == ShiftStatus.Exported, cancellationToken);
        return shift?.ExportId is Guid exportId ? (exportId, shift.Version) : null;
    }

    public async Task AuditDownloadAsync(Guid exportId, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, actorId, "download");
        Audit(db, nameof(PayrollExport), exportId, "Download", null, null, actorId, "download");
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Audit(FleetOpsDbContext db, string type, Guid id, string field, string? before, string? after, Guid actorId, string reason) =>
        db.AuditEntries.Add(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = type, EntityId = id, FieldName = field, OldValue = before,
            NewValue = after, ActorId = actorId.ToString(), Reason = reason, CreatedAt = clock.GetUtcNow() });

    private static FleetOpsDbContext Create(CommandContext context, Guid actorId = default, string reason = "")
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId, AuditReason = reason };
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}
