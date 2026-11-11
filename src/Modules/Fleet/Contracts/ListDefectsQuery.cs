using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[QueryPolicy("defects.manage")]
public sealed record ListDefectsQuery(int Page = 1, int PageSize = 50, string? StatusGroup = null, DefectSeverity? Severity = null, Guid? VehicleId = null) : IQuery<DefectListResult>;
