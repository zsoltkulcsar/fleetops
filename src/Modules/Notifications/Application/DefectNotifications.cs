using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Notifications;
public sealed class DefectNotifications(IDefectNotificationFacts facts, INotifications notifications, IChangedSubjects changed) : IDefectEvents
{
    public async Task ReportedAsync(DefectReport defect, bool becameOffRoad, CommandContext context, CancellationToken cancellationToken)
    {
        changed.Add("defect", defect.Id);
        changed.Add("vehicle", defect.VehicleId);
        if (!becameOffRoad) return;
        foreach (var employeeId in await facts.DriversAsync(defect.VehicleId, context, cancellationToken))
        {
            if (employeeId != defect.ReportedBy)
                await notifications.AddAsync(employeeId, "vehicle_unusable", new { vehicleId = defect.VehicleId }, null, context, cancellationToken);
        }
    }
    public async Task CancelledAsync(DefectReport defect, CommandContext context, CancellationToken cancellationToken)
    {
        changed.Add("defect", defect.Id);
        changed.Add("vehicle", defect.VehicleId);
        foreach (var employeeId in await facts.DispatchersAsync(context, cancellationToken))
            await notifications.AddAsync(employeeId, "defect_cancelled", new { defectId = defect.Id, vehicleId = defect.VehicleId }, null, context, cancellationToken);
    }
}
