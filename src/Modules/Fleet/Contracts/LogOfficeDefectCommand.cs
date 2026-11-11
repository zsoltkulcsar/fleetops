using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[CommandPolicy("defects.manage")]
public sealed record LogOfficeDefectCommand(Guid VehicleId, DefectCategory Category, DefectSeverity Severity, string? Description,
    Guid? ReportedBy = null, Guid[]? Photos = null) : ICommand<DefectActionResult>;
