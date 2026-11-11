using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
[CommandPolicy("vehicles.release")]
public sealed record ReleaseVehicleCommand(Guid VehicleId, string? Reason, DateTimeOffset? ValidUntil = null) : ICommand<VehicleReleaseResult>;
