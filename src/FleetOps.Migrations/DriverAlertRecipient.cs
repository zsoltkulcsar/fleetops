using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class DriverAlertRecipient(IConfiguration configuration) : IDriverAlertRecipient
{
    public async Task<Guid?> ShiftOwnerAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        return await db.Shifts.AsNoTracking().Where(shift => shift.Id == shiftId).Select(shift => (Guid?)shift.EmployeeId).SingleOrDefaultAsync(cancellationToken);
    }
}
