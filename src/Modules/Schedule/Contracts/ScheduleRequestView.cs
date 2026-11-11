using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ScheduleRequestView(Guid Id, Guid EmployeeId, Guid CycleId, DateOnly ProposedFrom, DateOnly ProposedTo, string Note, ScheduleRequestStatus Status, string? DeclineReason, Guid? DecidedBy, DateTimeOffset? DecidedAt) : IReadModel;
