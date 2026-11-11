using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Views;
[VisibleTo(RoleCode.Driver)]
public sealed record DriverSwap(Guid Id, Guid VehicleId, string VehiclePlate, DateOnly From, DateOnly? To);
