using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
public sealed class AccessConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
