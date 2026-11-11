using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PhoneChangeResult(string Code, PhoneChangeTestInbox? TestInbox = null) : IReadModel;
