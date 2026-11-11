using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
public sealed class VehicleReleaseConflictException(string code, IReadOnlyList<string>? types = null) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyList<string> Types { get; } = types ?? [];
}
