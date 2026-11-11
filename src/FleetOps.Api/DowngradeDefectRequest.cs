using FleetOps.Modules.Fleet;
namespace FleetOps.Api;

public sealed record DowngradeDefectRequest(DefectSeverity Severity, string? Reason);
