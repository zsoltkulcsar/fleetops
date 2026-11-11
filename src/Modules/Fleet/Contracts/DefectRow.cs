using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record DefectRow(Guid Id, Guid VehicleId, string Plate, DefectCategory Category, DefectSeverity Severity,
    Guid ReportedBy, string ReportedByName, DateTimeOffset ReportedAt, int PhotosCount, DefectStatus Status, double AgeDays, DefectOrigin Origin);
