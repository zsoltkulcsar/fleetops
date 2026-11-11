using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record NextServiceProposal(Guid VehicleId, string Type, DateOnly? DueDate, int? DueKm);
