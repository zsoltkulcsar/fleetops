using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PeoplePage(IReadOnlyList<PersonRow> Items, int Page, int PageSize, int Total) : IReadModel;
