using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record PersonSaveResult(Guid Id, string Status, bool EnrolmentSent = false) : IReadModel;
