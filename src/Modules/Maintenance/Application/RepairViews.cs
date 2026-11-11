using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public static class RepairViews
{
    public static RepairDetail Detail(Maintenance repair) => new(repair.Id, repair.Type, repair.VehicleId, repair.Workshop,
        repair.ScheduledFor, repair.DefectReportIds, repair.Status, repair.CancelReason, repair.DoneAt,
        repair.OdometerAtDoneKm, repair.Status == MaintenanceStatus.InProgress);
    public static AssignmentDetail Detail(Assignment assignment) => new(assignment.Id, assignment.EmployeeId,
        assignment.VehicleId, assignment.From, assignment.To, assignment.MaintenanceId);
}
