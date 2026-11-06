using FleetOps.Kernel.Authorization;

namespace FleetOps.Api.Auth;

internal static class SecondFactorPolicy
{
    public static bool IsRequired(IReadOnlySet<RoleCode> roles, AuthOptions options, IHostEnvironment environment)
    {
        var privilegedRole = roles.Contains(RoleCode.Hr) || roles.Contains(RoleCode.Owner);
        return privilegedRole && (options.RequireSecondFactor || !environment.IsEnvironment("Demo"));
    }
}
