using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;
[QueryPolicy("schedule.view")]
public sealed record ListScheduleRequestsQuery : IQuery<ScheduleRequestList>;
