using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ScheduleRequestList(IReadOnlyList<ScheduleRequestView> Items) : IReadModel;
