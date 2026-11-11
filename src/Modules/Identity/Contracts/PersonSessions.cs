using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PersonSessions(IReadOnlyList<PersonSession> Items) : IReadModel;
