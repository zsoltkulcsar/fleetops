using FleetOps.Kernel.Authorization;

namespace FleetOps.Identity;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record EnrolmentTestInbox(string Code, string PersonnelNumber);
