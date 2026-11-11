using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PersonRow(Guid Id, string Name, string[] Roles, string Status, bool CanDrive,
    string? ReasonCategory, int ValidDocuments, int RequiredDocuments, string? WorstState,
    string? DefaultVehiclePlate, string? Phone);
