using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class ScheduleChangedNotifications(INotifications notifications) : IScheduleChanged
{
    public async Task NotifyAsync(CycleMutation mutation, CommandContext context, CancellationToken cancellationToken)
    {
        foreach (var employeeId in new Guid?[] { mutation.Detail.EmployeeId, mutation.PreviousEmployeeId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct())
            await notifications.AddAsync(employeeId, "schedule_changed", new { cycleId = mutation.Detail.Id }, null, context, cancellationToken);
    }
}

