using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
internal static class OfficeNotificationAccess
{
    public static void Demand(AccessContext actor)
    {
        if (!actor.Roles.Any(role => role is RoleCode.Hr or RoleCode.Dispatcher or RoleCode.Owner))
            throw new AuthorizationException(RefusalCodes.PermissionDenied, "notifications.mine", [RoleCode.Hr, RoleCode.Dispatcher, RoleCode.Owner]);
    }
}
