using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfShiftAlertFacts : IShiftAlertFacts
{
    public async Task<ShiftAlertFacts> ReadAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        var blocked = await db.Shifts.Where(shift => shift.EmployeeId == employeeId && shift.BlockedAtStart != null && shift.Status != ShiftStatus.Approved && shift.Status != ShiftStatus.Exported)
            .Select(shift => shift.Id).ToListAsync(cancellationToken);
        var conflicts = await db.ShiftConflicts.Where(conflict => conflict.EmployeeId == employeeId).Select(conflict => conflict.ShiftId).Distinct().ToListAsync(cancellationToken);
        return new(blocked, conflicts);
    }
}
