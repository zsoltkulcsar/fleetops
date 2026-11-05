using FleetOps.Kernel.Authorization;

namespace FleetOps.Api.Pipeline;

public sealed class NoSessionAccessor : IAccessContextAccessor
{
    public AccessContext? Current => null;
}
