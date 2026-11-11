using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
public sealed record VehicleReleaseFacts(bool ActiveTruck, IReadOnlyList<ReadinessCause> Causes);
