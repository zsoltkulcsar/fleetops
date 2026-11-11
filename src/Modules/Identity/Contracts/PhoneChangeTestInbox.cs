using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PhoneChangeTestInbox(string Phone, string Message);
