using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PersonDetail(PersonRow Header, PersonFields Fields, Guid? DepotId,
    IReadOnlyList<ChecklistItem> Checklist, int VerifiedCount, int RequiredCount) : IReadModel;
