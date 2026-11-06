namespace FleetOps.Kernel.Authorization;

public enum RoleCode { Driver, Dispatcher, Hr, Owner }

public static class RoleCodeExtensions
{
    public static string Code(this RoleCode role) => role switch
    {
        RoleCode.Driver => "driver",
        RoleCode.Dispatcher => "dispatcher",
        RoleCode.Hr => "hr",
        RoleCode.Owner => "owner",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}

public sealed record AccessContext(Guid EmployeeId, IReadOnlySet<RoleCode> Roles)
{
    public bool HasAnyRole(IEnumerable<RoleCode> roles) => roles.Any(Roles.Contains);
    public static AccessContext For(Guid employeeId, params RoleCode[] roles) => new(employeeId, roles.ToHashSet());
}

public interface IAccessContextAccessor
{
    AccessContext? Current { get; }
}

public interface IStepUpContextAccessor
{
    bool HasFreshStepUp { get; }
}
