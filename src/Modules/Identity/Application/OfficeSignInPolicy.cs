using FleetOps.Kernel.Authorization;

namespace FleetOps.Identity;

public static class OfficeSignInPolicy
{
    public static bool IsAllowed(IReadOnlySet<RoleCode> roles) =>
        roles.Any(role => role is RoleCode.Owner or RoleCode.Hr or RoleCode.Dispatcher);
}
